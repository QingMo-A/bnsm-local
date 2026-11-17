using System.Security.Cryptography;

// This version retained the five extraction weights but not the old server's
// tier/quantity formula. The local policy is explicitly one single-attribute
// stone at equipment Rank (capped at the highest original single stone).
internal sealed class EngravingRules(GameCatalog catalog)
{
    public static readonly BscTable.EQEFFECT_TYPE[] Attributes = BscTable.ItemData.empowerType;
    private BscTable.ItemData[]? stones;
    private readonly Dictionary<int,BscTable.ItemData[]> pools=[];
    public IEnumerable<BscTable.ItemData> ExtractionPool(BscTable.ItemData gear)
    {
        int limit=Math.Max(1,gear.Rank);
        if(pools.TryGetValue(limit,out var cached))return cached;
        stones??=catalog.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&x.EmpowerFlag&&x.AttributeValueIdType.Count==1).ToArray();
        int rank=stones.Where(x=>x.Rank<=limit).Max(x=>x.Rank);
        return pools[limit]=stones.Where(x=>x.Rank==rank).ToArray();
    }
    public int Weight(BscTable.ItemData stone)
    {
        int index=Array.IndexOf(Attributes,stone.AttributeValueIdType.Single());
        BscTable.NUMBER_TYPE[] keys=[BscTable.NUMBER_TYPE.EMPOWERSTONE_EXTRACT_RATE_HP,BscTable.NUMBER_TYPE.EMPOWERSTONE_EXTRACT_RATE_PATK,BscTable.NUMBER_TYPE.EMPOWERSTONE_EXTRACT_RATE_FATK,BscTable.NUMBER_TYPE.EMPOWERSTONE_EXTRACT_RATE_PDEF,BscTable.NUMBER_TYPE.EMPOWERSTONE_EXTRACT_RATE_FDEF];
        return index>=0&&index<keys.Length?(int)catalog.Numbers.GetValueOrDefault(keys[index]):0;
    }
    public uint Extract(BscTable.ItemData gear)
    {
        var pool=ExtractionPool(gear).Select(x=>(item:x,weight:Weight(x))).Where(x=>x.weight>0).ToArray();
        int roll=RandomNumberGenerator.GetInt32(pool.Sum(x=>x.weight));
        foreach(var row in pool) {roll-=row.weight;if(roll<0)return (uint)row.item.Id;}
        throw new InvalidDataException("Missing original extraction stone weights");
    }
}

internal sealed partial class ProtocolRouter
{
    private Reply? HandleEngraving(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_EXTRACT_EQUIPMENT_ITEM_REQUEST" or "GS_EMPOWER_EQUIPMENT_ITEM_REQUEST" or "GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST" or "GS_EMPOWER_STONE_CRAFT_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.EquipmentReceipts.TryGetValue(key,out var saved))return saved.Fingerprint==fingerprint
            ?new(saved.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(saved.TypeName,true),Convert.FromBase64String(saved.Payload)),saved.Name)
            :fail("equipment sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        if(p.ActiveStage!=null)return fail("finish the current battle before changing equipment",ServerErrorCode.ERROR_INVALID_ID);
        Reply result;
        if(request is GsEmpowerStoneCraftRequest craft)result=CraftEmpowerStone(p,craft,ok,fail);
        else {
            ulong seq=request switch {LocalEquipmentRequest r=>r.equipmentItemSeq,GsEmpowerEquipmentItemRequest r=>r.equipmentItemSeq,GsResetEmpowerEquipmentItemRequest r=>r.equipmentItemSeq,_=>0};
            var owned=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==seq);
            var item=owned==null?null:catalog.Items.FirstOrDefault(x=>x.Id==owned.ItemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT);
            if(item==null)return fail("equipment instance is not owned",ServerErrorCode.ERROR_INVALID_ID);
            ulong owner=EquipmentOwner(p,seq);
            if(owner!=0&&InTraining(p,owner))return fail("finish training before changing equipment",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            if(request is LocalEquipmentRequest) {
                if(owned!.Locked)return fail("locked equipment cannot be disassembled",ServerErrorCode.ERROR_INVALID_ID);
                if(!item.Extraction||owner!=0)return fail("unequip this item before disassembling it",ServerErrorCode.ERROR_INVALID_ID);
                var rules=new EngravingRules(catalog);
                // Check every possible output before drawing or deleting anything.
                if(rules.ExtractionPool(item).Any(x=>p.Inventory.GetValueOrDefault((uint)x.Id)>=999999999)||p.Inventory.Count(x=>x.Value>0&&(catalog.ItemsById.TryGetValue((int)x.Key,out var stackItem)&&stackItem.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT))>=994)
                    return fail("not enough inventory space for extraction",ServerErrorCode.ERROR_NOT_ENOUGH_INVEN_SLOT);
                uint stone=rules.Extract(item);
                var delta=EquipmentInfo(p,owned!);delta.itemCount=-1;
                p.EquipmentItems.Remove(owned!);p.Inventory[stone]=p.Inventory.GetValueOrDefault(stone)+1;
                var extracted=new GsExtractEquipmentItemOk{userCoinInfo=Coins(p)};extracted.deltaItemList.Add(delta);extracted.deltaItemList.Add(ItemInfo(stone,1));result=ok(extracted);
            } else {
                if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_SET_EMPOWER))return fail("engraving unlocks at original player level 22",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
                if(!item.Empower||item.EmpowerMaxCost<=0||item.Rank<catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.EMPOWERSTONE_LIMIT_RANK))return fail("equipment cannot be engraved",ServerErrorCode.ERROR_INVALID_ID);
                if(request is GsResetEmpowerEquipmentItemRequest) {
                    if(owned!.CurrentEmpowerValue==0)return fail("equipment has no engraving to reset",ServerErrorCode.ERROR_INVALID_ID);
                    owned.CurrentEmpowerValue=0;owned.EmpowerValues=Enumerable.Repeat(0u,12).ToList();
                    var reset=new GsResetEmpowerEquipmentItemOk();var delta=EquipmentInfo(p,owned);delta.itemCount=0;reset.deltaItemList.Add(delta);result=ok(reset);
                } else result=EmpowerEquipment(p,owned!,item,(GsEmpowerEquipmentItemRequest)request!,ok,fail);
            }
        }
        if(result.Name.EndsWith("_OK")) {
            if(p.EquipmentReceipts.Count>=256)p.EquipmentReceipts.Remove(p.EquipmentReceipts.Keys.First());
            p.EquipmentReceipts[key]=new(){Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};store.Save();
        }
        return result;
    }
    private Reply EmpowerEquipment(LocalPlayer p,LocalOwnedEquipment owned,BscTable.ItemData gear,GsEmpowerEquipmentItemRequest request,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if((p.DebugFlags&LocalDebugFlags.FreeEquipmentEmpower)!=0)return DebugEmpowerEquipment(p,owned,gear,request,ok,fail);
        var ids=request.empowerStoneItemIdList;
        if(ids.Count is 0 or >999)return fail("select engraving stones first",ServerErrorCode.ERROR_INVALID_ID);
        ulong value=owned.CurrentEmpowerValue,gold=0;
        var attributes=Enumerable.Range(0,12).Select(i=>(ulong)(i<owned.EmpowerValues.Count?owned.EmpowerValues[i]:0)).ToArray();
        var groups=ids.GroupBy(x=>x).ToArray();
        foreach(var group in groups) {
            var stone=catalog.Items.FirstOrDefault(x=>x.Id==group.Key&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE);
            if(stone==null||stone.EmpowerValue<=0||stone.EmpowerCost<0||stone.EmpowerLimitItemLevel>gear.Level||stone.AttributeValueId.Count!=stone.AttributeValueIdType.Count)return fail("invalid engraving stone",ServerErrorCode.ERROR_INVALID_ID);
            if(p.Inventory.GetValueOrDefault(group.Key)<group.Count())return fail("not enough engraving stones",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
            value+=(ulong)stone.EmpowerValue*(uint)group.Count();gold+=(ulong)stone.EmpowerCost*(uint)group.Count();
            for(int i=0;i<stone.AttributeValueId.Count;i++) {
                int index=Array.IndexOf(EngravingRules.Attributes,stone.AttributeValueIdType[i]);
                if(index<0||stone.AttributeValueId[i]<0)return fail("unsupported engraving attribute",ServerErrorCode.ERROR_INVALID_ID);
                attributes[index]+=(ulong)stone.AttributeValueId[i]*(uint)group.Count();
            }
        }
        if(value>(uint)gear.EmpowerMaxCost||attributes.Any(x=>x>uint.MaxValue))return fail("engraving would exceed the equipment cap",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(gold>p.Gold)return fail("not enough gold for engraving",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        p.Gold-=(uint)gold;p.Ranking.EngravingTotal+=value-owned.CurrentEmpowerValue;owned.CurrentEmpowerValue=(uint)value;owned.EmpowerValues=attributes.Select(x=>(uint)x).ToList();
        var result=new GsEmpowerEquipmentItemOk{userCoinInfo=Coins(p)};
        foreach(var group in groups) {p.Inventory[group.Key]-=group.Count();if(p.Inventory[group.Key]==0)p.Inventory.Remove(group.Key);result.deltaItemList.Add(ItemInfo(group.Key,-group.Count()));}
        var metadata=EquipmentInfo(p,owned);metadata.itemCount=0;result.deltaItemList.Add(metadata);return ok(result);
    }
    private Reply CraftEmpowerStone(LocalPlayer p,GsEmpowerStoneCraftRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var source=catalog.Items.FirstOrDefault(x=>x.Id==r.BaseItemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE);
        var target=catalog.Items.FirstOrDefault(x=>x.Id==r.TargetItemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE);
        if(source==null||target==null||source.Next1Id!=target.Id||target.PrevId!=source.Id||source.Material1Id.Count==0||source.Material1Id.Count!=source.Material1Value.Count||source.Rank>=catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_EMPOWER))return fail("invalid engraving stone recipe",ServerErrorCode.ERROR_INVALID_ID);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_EMPOWER))return fail("engraving stone crafting unlocks at original level 26",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var rows=catalog.CardProcesses.List.First(x=>x.ProcessType==BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EMPOWER_STONE_CRAFT).ProcessDataList;
        var process=rows[Math.Clamp(target.Rank-1,0,Math.Min(9,rows.Count-1))];
        if((p.DebugFlags&LocalDebugFlags.FreeStoneCraft)!=0) {
            if(process.Probability!=100)return fail("unsupported engraving stone process",ServerErrorCode.ERROR_INVALID_ID);
            target=DebugStackTarget(p,source,target,false);
            var debugPlan=new CraftPlan(p,catalog);
            if(!debugPlan.Consume(r.BaseItemId,1,false)||!debugPlan.Add((uint)target.Id,1))return fail(debugPlan.Error,debugPlan.Code);
            var response=new GsEmpowerStoneCraftOk{result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p)};response.userItemList.AddRange(debugPlan.Commit());return ok(response);
        }
        var materials=new Dictionary<uint,long>{{r.BaseItemId,1}};
        for(int i=0;i<source.Material1Id.Count;i++) {
            uint id=(uint)source.Material1Id[i];var item=catalog.Items.FirstOrDefault(x=>x.Id==id);
            if(source.Material1Value[i]<=0||item==null||item.ItemType is not (BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE or BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT))return fail("invalid recipe material",ServerErrorCode.ERROR_INVALID_ID);
            materials[id]=materials.GetValueOrDefault(id)+source.Material1Value[i];
        }
        var plan=new CraftPlan(p,catalog);
        if(!plan.Cost(process.CostGold))return fail(plan.Error,plan.Code);
        foreach(var pair in materials)if(!plan.Consume(pair.Key,pair.Value,true))return fail(plan.Error,plan.Code);
        if(!plan.Add(r.TargetItemId,1)||plan.Inventory[r.TargetItemId]>999999999)return fail("stone inventory capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsEmpowerStoneCraftOk{result=CraftResult.CRAFT_RESULT_SUCCESS};result.userItemList.AddRange(plan.Commit());result.nowCoinInfo=Coins(p);return ok(result);
    }
}
