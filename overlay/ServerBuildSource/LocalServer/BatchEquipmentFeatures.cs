using System.Security.Cryptography;
using System.Text;

internal sealed partial class ProtocolRouter
{
    private static bool ImprovedEquipment(LocalOwnedEquipment owned,BscTable.ItemData item) =>
        owned.CurrentEmpowerValue!=0 || owned.EmpowerValues.Any(x=>x!=0) || item.PrevId!=0 || item.Step>1 ||
        item.EquipmentType>BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL || item.Grade>=BscTable.ITEM_GRADE.ITEM_GRADE_HERO;

    private string EquipmentPreviewToken(LocalPlayer p,IEnumerable<LocalOwnedEquipment> selected,bool allow)
    {
        var text=new StringBuilder($"equipment-v1/{p.Uid}/{allow}");
        foreach(var item in selected.OrderBy(x=>x.Sequence))text.Append($"/{item.Sequence}:{item.ItemId}:{item.Locked}:{EquipmentOwner(p,item.Sequence)}:{item.CurrentEmpowerValue}:{string.Join(',',item.EmpowerValues)}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private Reply? HandleBatchEquipment(string name,object? request,LocalPlayer p,SessionKeyInfo session,
        Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name!="GS_EXTRACT_EQUIPMENT_ITEM_REQUEST" || request is not LocalEquipmentRequest r || r.operation==0)return null;
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);
        if(r.operation>LocalEquipmentRequest.Extract || r.equipmentItemSeq!=0 || r.isTutorial || r.sequences.Count is 0 or >LocalEquipmentRequest.Maximum || r.sequences.Any(x=>x==0) || r.sequences.Distinct().Count()!=r.sequences.Count)
            return Invalid("invalid batch equipment selection (1..200 unique instances)");
        bool mutation=r.operation is LocalEquipmentRequest.Lock or LocalEquipmentRequest.Unlock or LocalEquipmentRequest.Extract;
        string key="",fingerprint="";
        if(mutation) {
            if(!Guid.TryParseExact(r.transactionId,"N",out _))return Invalid("batch transaction identifier is required");
            key=r.transactionId;
            fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{r.operation}/{r.allowImproved}/{r.previewToken}/{string.Join(',',r.sequences)}")));
            if(p.EquipmentBatchReceipts.TryGetValue(key,out var receipt))return receipt.Fingerprint==fingerprint
                ?new(receipt.Command,WireCodec.Deserialize(typeof(LocalEquipmentReply),Convert.FromBase64String(receipt.Payload)),receipt.Name)
                :Invalid("batch transaction identifier reused with different parameters");
        }
        // Resolve all unique instances and all outputs before touching the save.
        var selected=new List<LocalOwnedEquipment>();
        foreach(ulong seq in r.sequences) {
            var owned=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==seq);
            if(owned==null)return Invalid("equipment selection changed; refresh the inventory");
            selected.Add(owned);
        }
        var result=new LocalEquipmentReply{userCoinInfo=Coins(p)};
        if(r.operation==LocalEquipmentRequest.State) {result.locked.AddRange(selected.Where(x=>x.Locked).Select(x=>x.Sequence));return ok(result);}
        if(p.ActiveStage!=null)return Invalid("finish the current battle before changing equipment");
        if(r.operation is LocalEquipmentRequest.Lock or LocalEquipmentRequest.Unlock) {
            foreach(var item in selected)item.Locked=r.operation==LocalEquipmentRequest.Lock;
            result.locked.AddRange(selected.Where(x=>x.Locked).Select(x=>x.Sequence));
        } else {
            var rules=new EngravingRules(catalog);
            var pools=new Dictionary<ulong,BscTable.ItemData[]>();
            foreach(var owned in selected) {
                var item=catalog.ItemsById.GetValueOrDefault((int)owned.ItemId);
                if(item==null||item.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT||!item.Extraction)return Invalid("this equipment cannot be disassembled");
                if(owned.Locked)return Invalid("locked equipment cannot be disassembled");
                if(EquipmentOwner(p,owned.Sequence)!=0)return Invalid("unequip every selected item before disassembly");
                if(!r.allowImproved&&ImprovedEquipment(owned,item))return Invalid("improved or high-quality equipment requires explicit selection");
                pools[owned.Sequence]=rules.ExtractionPool(item).Where(x=>rules.Weight(x)>0).ToArray();
                if(pools[owned.Sequence].Length==0)return Invalid("missing extraction rewards");
            }
            foreach(var group in selected.GroupBy(x=>pools[x.Sequence][0].Rank).OrderBy(x=>x.Key))result.tiers.Add(new(){rank=group.Key,count=group.Count()});
            result.previewToken=EquipmentPreviewToken(p,selected,r.allowImproved);
            if(r.operation==LocalEquipmentRequest.Preview)return ok(result);
            if(r.previewToken!=result.previewToken)return Invalid("equipment changed after preview; review the selection again");
            // Capacity is checked for the worst possible random roll, including new stacks.
            var possible=pools.Values.SelectMany(x=>x).GroupBy(x=>(uint)x.Id).ToArray();
            foreach(var group in possible)if((long)p.Inventory.GetValueOrDefault(group.Key)+group.Count()>999999999)
                return fail("not enough stack space for extraction",ServerErrorCode.ERROR_NOT_ENOUGH_INVEN_SLOT);
            int slots=p.Inventory.Count(x=>x.Value>0&&(catalog.ItemsById.TryGetValue((int)x.Key,out var stackItem)&&stackItem.ItemType is BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT or BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE));
            if(slots+possible.Count(x=>p.Inventory.GetValueOrDefault(x.Key)<=0)>1000)return fail("not enough material inventory slots",ServerErrorCode.ERROR_NOT_ENOUGH_INVEN_SLOT);
            var rewards=new Dictionary<uint,int>();
            foreach(var owned in selected) {
                uint stone=rules.Extract(catalog.ItemsById[(int)owned.ItemId]);
                rewards[stone]=rewards.GetValueOrDefault(stone)+1;
                // A removal needs only identity and count, keeping 200-item replies below 8192 bytes.
                result.deltaItemList.Add(new(){itemID=owned.ItemId,itemSeq=owned.Sequence,itemCount=-1});
            }
            result.deltaItemList.AddRange(rewards.OrderBy(x=>x.Key).Select(x=>ItemInfo(x.Key,x.Value)));
            // Check the actual encoded response before committing any mutation.
            if(WireCodec.Serialize(result).Length+WireCodec.HeaderSize>WireCodec.MaxPacketSize)return Invalid("batch response too large; select fewer items");
            foreach(var owned in selected)p.EquipmentItems.Remove(owned);
            foreach(var pair in rewards)p.Inventory[pair.Key]=p.Inventory.GetValueOrDefault(pair.Key)+pair.Value;
        }
        var reply=ok(result);
        if(p.EquipmentBatchReceipts.Count>=256)p.EquipmentBatchReceipts.Remove(p.EquipmentBatchReceipts.Keys.First());
        p.EquipmentBatchReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=typeof(LocalEquipmentReply).FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result))};
        store.Save();return reply;
    }
}
