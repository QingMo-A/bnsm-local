// Debug growth uses the same versioned links, ownership, unlocks and receipts as
// normal requests. Only catalyst/currency costs are waived; an owned base stays required.
internal sealed partial class ProtocolRouter
{
    private BscTable.ItemData DebugEquipmentTarget(LocalPlayer p,BscTable.ItemData source,BscTable.ItemData first,bool awake,ulong wearer)
    {
        uint max=awake?LocalDebugFlags.MaxEquipmentAwake:LocalDebugFlags.MaxEquipmentCraft;
        if((p.DebugFlags&max)==0)return first;
        var current=first;var visited=new HashSet<int>{source.Id,first.Id};
        while(visited.Count<128) {
            int id=awake?current.AwakningNextId:current.Next1Id;
            if(!catalog.ItemsById.TryGetValue(id,out var next)||next.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT||visited.Contains(id))break;
            // Refinement and awakening have separate toggles and never cross each other's links.
            if(!awake&&next.EquipmentType!=current.EquipmentType)break;
            var gate=awake?current.EquipmentType switch {
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_AWAKE_EXTREME,
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_AWAKE_MORE_EXTREME,
                _=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_CHANGE}:BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_EQUIPMENT;
            if(!FeatureOpen(p,gate)||CraftProcess(next,awake)?.Probability!=100)break;
            if(wearer!=0) {
                var card=catalog.RequireCard(p.CardIds[(int)wearer-1]);
                if(!FitsEquipment(next,card,p.CardStates.GetValueOrDefault(wearer)?.Level??1))break;
            }
            visited.Add(id);current=next;
        }
        return current;
    }
    private BscTable.ItemData DebugStackTarget(LocalPlayer p,BscTable.ItemData source,BscTable.ItemData first,bool gem)
    {
        if((p.DebugFlags&(gem?LocalDebugFlags.MaxGemCraft:LocalDebugFlags.MaxStoneCraft))==0)return first;
        int limit=catalog.ContentValue(gem?BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_GEM:BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_EMPOWER);
        var current=first;var visited=new HashSet<int>{source.Id,first.Id};
        var rows=gem?null:catalog.CardProcesses.List.First(x=>x.ProcessType==BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EMPOWER_STONE_CRAFT).ProcessDataList;
        while(visited.Count<128&&(gem?current.Step:current.Rank)<limit) {
            if(!catalog.ItemsById.TryGetValue(current.Next1Id,out var next)||next.ItemType!=current.ItemType||next.PrevId!=current.Id||visited.Contains(next.Id))break;
            if((gem?next.Step:next.Rank)<=(gem?current.Step:current.Rank)||current.Material1Id.Count==0||current.Material1Id.Count!=current.Material1Value.Count)break;
            if(gem?CraftProcess(next)?.Probability!=100:rows![Math.Clamp(next.Rank-1,0,Math.Min(9,rows.Count-1))].Probability!=100)break;
            visited.Add(next.Id);current=next;
        }
        return current;
    }
    private Reply DebugEmpowerEquipment(LocalPlayer p,LocalOwnedEquipment owned,BscTable.ItemData gear,GsEmpowerEquipmentItemRequest request,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string message)=>fail(message,ServerErrorCode.ERROR_INVALID_ID);
        uint cap=(uint)gear.EmpowerMaxCost;
        if(owned.CurrentEmpowerValue>=cap)return fail("engraving is already at its original cap",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var ids=request.empowerStoneItemIdList;
        if(ids.Count>999)return Invalid("too many engraving stones");
        bool Valid(BscTable.ItemData s)=>s.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&s.EmpowerValue>0&&s.EmpowerCost>=0&&s.EmpowerLimitItemLevel<=gear.Level&&s.AttributeValueId.Count>0&&s.AttributeValueId.Count==s.AttributeValueIdType.Count&&s.AttributeValueId.All(x=>x>=0)&&s.AttributeValueIdType.All(x=>Array.IndexOf(EngravingRules.Attributes,x)>=0);
        var selected=new List<BscTable.ItemData>();
        foreach(uint id in ids) {
            if(!catalog.ItemsById.TryGetValue((int)id,out var stone)||!Valid(stone))return Invalid("invalid engraving stone");
            selected.Add(stone);
        }
        // Without selected stones use a real single-attribute stone. Never invent
        // attribute points to fill an otherwise unreachable remainder.
        var defaults=catalog.Items.Where(s=>Valid(s)&&s.AttributeValueIdType.Count==1).OrderBy(s=>s.EmpowerValue).ThenBy(s=>s.Id).ToArray();
        ulong value=owned.CurrentEmpowerValue;
        var attributes=Enumerable.Range(0,12).Select(i=>(ulong)(i<owned.EmpowerValues.Count?owned.EmpowerValues[i]:0)).ToArray();
        bool Add(BscTable.ItemData stone,ulong count) {
            if(value+(ulong)stone.EmpowerValue*count>cap)return false;
            for(int i=0;i<stone.AttributeValueId.Count;i++) {
                int index=Array.IndexOf(EngravingRules.Attributes,stone.AttributeValueIdType[i]);
                attributes[index]+=(ulong)stone.AttributeValueId[i]*count;
                if(attributes[index]>uint.MaxValue)return false;
            }
            value+=(ulong)stone.EmpowerValue*count;return true;
        }
        if((p.DebugFlags&LocalDebugFlags.MaxEquipmentEmpower)!=0) {
            // Honour the selected attribute preference, then fill only with valid original stones.
            foreach(var stone in selected.Concat(defaults).DistinctBy(x=>x.Id)) {
                ulong count=(cap-value)/(uint)stone.EmpowerValue;
                if(count>0&&!Add(stone,count))return Invalid("engraving attribute capacity exceeded");
                if(value==cap)break;
            }
        } else if(selected.Count>0) {
            foreach(var stone in selected)if(!Add(stone,1))return fail("engraving would exceed the equipment cap",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        } else {
            var stone=defaults.FirstOrDefault(x=>(ulong)x.EmpowerValue<=cap-value);
            if(stone==null||!Add(stone,1))return Invalid("no compatible original engraving stone fits");
        }
        if(value==owned.CurrentEmpowerValue)return fail("no further valid engraving fits",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        p.Ranking.EngravingTotal+=value-owned.CurrentEmpowerValue;
        owned.CurrentEmpowerValue=(uint)value;owned.EmpowerValues=attributes.Select(x=>(uint)x).ToList();
        var response=new GsEmpowerEquipmentItemOk{userCoinInfo=Coins(p)};
        var delta=EquipmentInfo(p,owned);delta.itemCount=0;response.deltaItemList.Add(delta);return ok(response);
    }
}
