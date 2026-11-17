internal sealed partial class ProtocolRouter
{
    private Reply? HandleBsBall(string name,object? request,LocalPlayer p,SessionKeyInfo key,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(request is not (GsEquipBSBallRequest or GsChangeUseBSBallFlagRequest))return null;
        return FeatureReceipt(p,p.Competitive.Receipts,name,key,payload,()=>{
            if(request is GsChangeUseBSBallFlagRequest flag){p.BsBallUseFlag=flag.bsBallUseFlag;return ok(null);}
            var r=(GsEquipBSBallRequest)request!;
            Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);
            if(!Owns(p,r.cardSeq)||InTraining(p,r.cardSeq)||p.ActiveStage!=null)return Bad("finish training/battle before equipping ammunition");
            var state=CardState(p,r.cardSeq);var weapon=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==r.ItemSeq);
            if(weapon==null||state.WeaponSequence!=weapon.Sequence||catalog.ItemsById[(int)weapon.ItemId].ItemSubType!=BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON)return Bad("ammunition must attach to the owned equipped weapon");
            uint max=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BSBALL_EQUIP_MAX_COUNT],unit=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BSBALL_BATTLE_CONSUME_COUNT];
            if((r.bsBallId==0)!=(r.bsBallCount==0)||r.bsBallCount>max||unit==0||r.bsBallCount%unit!=0)return Bad("invalid ammunition quantity");
            if(r.bsBallId!=0&&(!catalog.ItemsById.TryGetValue((int)r.bsBallId,out var ball)||ball.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_BSBALL||!Competitive.ItemEffects.Any(x=>x.ItemId==r.bsBallId&&catalog.SkillIds.Contains(x.LinkSkillId))))return Bad("invalid original ammunition type");
            uint cost=r.bsBallId==0?0:(uint)Math.Max(0,catalog.MoneyResources.First(x=>x.MoneyEventType==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_BSBALL_EQUIP_COST).Value);
            if(p.Gold<cost)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            var inventory=p.Inventory.ToDictionary();
            if(weapon.BsBallCount>0){long count=(long)inventory.GetValueOrDefault(weapon.BsBallId)+weapon.BsBallCount;if(count>int.MaxValue)return Bad("ammunition inventory full");inventory[weapon.BsBallId]=(int)count;}
            if(r.bsBallCount>0){int have=inventory.GetValueOrDefault(r.bsBallId);if(have<r.bsBallCount)return fail("not enough ammunition",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);inventory[r.bsBallId]=have-(int)r.bsBallCount;}
            var changes=inventory.Keys.Union(p.Inventory.Keys).Select(id=>ItemInfo(id,inventory.GetValueOrDefault(id)-p.Inventory.GetValueOrDefault(id))).Where(x=>x.itemCount!=0).ToArray();
            p.Inventory=inventory;p.Gold-=cost;weapon.BsBallId=r.bsBallId;weapon.BsBallCount=r.bsBallCount;
            var result=new GsEquipBSBallOk{coinInfo=Coins(p)};result.deltaItemInfoList.AddRange(changes);var update=EquipmentInfo(p,weapon);update.itemCount=0;result.deltaItemInfoList.Add(update);return ok(result);
        },fail);
    }
    private void ConsumeBsBalls(LocalPlayer p,IEnumerable<ulong> cards)
    {
        if(!p.BsBallUseFlag)return;uint unit=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BSBALL_BATTLE_CONSUME_COUNT];
        foreach(ulong seq in cards.Distinct().Where(x=>Owns(p,x))) {
            var weapon=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==CardState(p,seq).WeaponSequence);
            if(weapon!=null&&weapon.BsBallId!=0&&unit>0&&weapon.BsBallCount>=unit)weapon.BsBallCount-=unit;
        }
        store.Save();
    }
    private PKBattleEndSyncData BallSync(LocalPlayer p)
    {
        var result=new PKBattleEndSyncData();result.bsBallDeltaItemInfoList.AddRange(p.EquipmentItems.Where(x=>x.BsBallId!=0).Select(x=>EquipmentInfo(p,x)));return result;
    }
    private void ObserveBsBallSuccess(LocalPlayer p,object? request,object? response)
    {
        if(request is GsNormalBattleStartRequest normal)ConsumeBsBalls(p,DeckSlots(p,normal.deckid==0?1u:normal.deckid));
        else if(request is GsResourceStageBattleStartRequest resource)ConsumeBsBalls(p,DeckSlots(p,resource.deckid));
        else if(request!=null&&request.GetType().Name.EndsWith("BattleStartRequest",StringComparison.Ordinal)&&p.Raids.ActiveStamp!=0)ConsumeBsBalls(p,p.Raids.BattleCards);
        var field=response?.GetType().GetField("battleEndSyncData");
        if(field!=null&&field.FieldType==typeof(PKBattleEndSyncData))field.SetValue(response,BallSync(p));
    }
}
