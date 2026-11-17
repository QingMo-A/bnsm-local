using System.Text.Json;
internal sealed class LocalResourceState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public Dictionary<uint,uint> Used {get;set;}=[];
    public Dictionary<uint,uint> Resets {get;set;}=[];
    public HashSet<uint> Cleared {get;set;}=[];
    public uint Stage {get;set;}
    public int Difficulty {get;set;}
    public ulong ActiveStamp {get;set;}
    public List<ulong> Cards {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    internal static void RefreshResourceDay(LocalResourceState r,ulong now) {ulong day=(now+8*3600)/86400;if(r.Day==day)return;r.Day=day;r.Used.Clear();r.Resets.Clear();}
    internal static bool ResourceSchedule(BscTable.ResourceStageLink row,ulong now)=>LocalResourceSchedule.IsOpen(row,now);
    internal bool ResourceOpen(BscTable.ResourceStageLink row,ulong now)=>Attendance.ResourceAllOpen(now)||ResourceSchedule(row,now);
    private byte[] ResourceClearArray(LocalPlayer p)
    {
        var result=Enumerable.Repeat((byte)255,(BoardResources.Resources.Max(x=>x.Id)+1)*2).ToArray();
        foreach(var row in BoardResources.Resources){int top=p.Resources.Cleared.Where(x=>x/10==row.Id).Select(x=>(int)(x%10)).DefaultIfEmpty(-1).Max();System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(row.Id*2,2),(short)top);}
        return result;
    }
    private void ClearResourceAttempt(LocalPlayer p)
    {
        var r=p.Resources;LocalBattleAttempts.ClearMatching(p,r.Stage,r.ActiveStamp);r.ActiveStamp=0;r.Stage=0;r.Cards.Clear();
    }
    private Reply? HandleResource(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool reset=request is GsMoneyTypeResourceChargeRequest charge&&charge.moneyTypeEvent==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_RESOURCE_STAGE_RESET_COST;
        if(!reset&&name is not ("GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST" or "GS_RESOURCE_STAGE_BATTLE_START_REQUEST" or "GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST" or "GS_RESOURCE_STAGE_SKIP_REQUEST"))return null;
        return FeatureReceipt(p,p.ResourceReceipts,name,session,payload,()=>ExecuteResource(name,request,p,ok,fail),fail);
    }
    private Reply ExecuteResource(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);var r=p.Resources;
        if(name=="GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST") {
            var result=new LocalResourceAvailabilityReply{isInAllOpenEvent=Attendance.ResourceAllOpen(Now),serverTime=Now,clearDifficulty=ResourceClearArray(p)};foreach(var row in BoardResources.Resources)result.useCount.Add(new(){id=row.Id,useCount=(int)r.Used.GetValueOrDefault((uint)row.Id),vipResetCount=(int)r.Resets.GetValueOrDefault((uint)row.Id)});return ok(result);
        }
        if(p.ActiveStage!=null&&name!="GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST")return Invalid("finish the active battle first");
        if(request is GsMoneyTypeResourceChargeRequest reset) {
            var row=BoardResources.Resources.FirstOrDefault(x=>x.Id==reset.param1);uint id=(uint)reset.param1;
            if(row==null)return Invalid("unknown resource reset stage: "+id);
            if(!ResourceOpen(row,Now))return fail("resource reset stage is outside its opening schedule; stage="+id,ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN);
            if(p.Level<row.RequirePcLevel[0]||r.Used.GetValueOrDefault(id)<row.EnterCount||r.Resets.GetValueOrDefault(id)>=catalog.VipFor(p.VipExp).ResourceMaxResetCount)return Invalid("resource reset is unavailable");
            var price=catalog.MoneyResources.Single(x=>x.MoneyEventType==reset.moneyTypeEvent);if(price.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_CASH||price.Value<0||p.CashPoint<price.Value)return fail("not enough reset diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)price.Value;r.Resets[id]=r.Resets.GetValueOrDefault(id)+1;r.Used[id]=0;return ok(new GsMoneyTypeResourceChargeOk{nowCoinInfo=Coins(p)});
        }
        if(request is GsResourceStageBattleEndConfirmRequest end) {
            if(r.Stage!=end.stageId||r.Difficulty!=(int)end.difficulty||r.ActiveStamp!=end.timeStamp||!LocalBattleAttempts.Matches(p,r.Stage,r.ActiveStamp)||!Enum.IsDefined(end.endType))return Invalid("no matching resource battle");
            if(end.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS&&end.enemyAliveCount!=unchecked((uint)((r.ActiveStamp%1000)*r.Stage)))return Invalid("resource victory has living enemies");
            var grant=new ShopGrant();uint exp=0;if(end.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS){var reward=BoardResources.Rewards[r.Difficulty].Single(x=>x.StageId==r.Stage);exp=(uint)reward.RewardExp;if(!ResourceReward(p,reward,!r.Cleared.Contains(r.Stage*10+(uint)r.Difficulty),grant))return Invalid("original resource reward unavailable");}
            var growth=StageGrowth(p,exp,0);if(growth==null||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("resource reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsResourceStageBattleEndConfirmOk{earnedExp=exp,earnedCoinInfo=GrantCoins(grant),battleEndSyncData=new()};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;
            if(end.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS){r.Cleared.Add(r.Stage*10+(uint)r.Difficulty);RecordResourceObjective(p,r.Stage,true);}ClearResourceAttempt(p);
            result.exp=p.Exp;result.level=p.Level;result.coinInfo=Coins(p);result.stamina=p.Stamina;result.staminaMax=catalog.StaminaMax(p.Level);result.staminaStamp=p.StaminaStamp;return ok(result);
        }
        uint stage=request is GsResourceStageBattleStartRequest start?start.stageid:request is GsResourceStageSkipRequest skip?skip.stageId:0;int diff=request is GsResourceStageBattleStartRequest begin?(int)begin.difficulty:request is GsResourceStageSkipRequest sweep?(int)sweep.difficulty:-1;
        var link=BoardResources.Resources.FirstOrDefault(x=>x.Id==stage);if(link==null||diff<0||diff>=6)return Invalid($"unknown resource stage or difficulty; stage={stage}; difficulty={diff}");
        if(!ResourceOpen(link,Now))return fail($"resource stage is outside its opening schedule; stage={stage}; difficulty={diff}; serverUtc={Now}; allOpenEvent=false",ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN);
        if(p.Level<link.RequirePcLevel[diff])return fail("original resource difficulty level not reached",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        uint count=request is GsResourceStageSkipRequest times?times.skipCount:1;
        if(count==0||count>100||(ulong)r.Used.GetValueOrDefault(stage)+count>(ulong)link.EnterCount)return fail("no daily resource entries remaining",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        ulong stamina=(ulong)link.ConsumeStamina*count;if(stamina>p.Stamina)return fail("not enough resource-stage stamina",ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
        if(link.ConsumeGold!=0||link.ConsumeGem!=0||link.ConsumeSoulStone!=0)return Invalid("unsupported original resource entry currency");
        if(request is GsResourceStageBattleStartRequest battle) {
            if(battle.deckid!=(uint)link.UseDeck||!ValidLocalRoster(p,battle.deckid))return Invalid("invalid resource-stage deck");
            var cards=DeckSlots(p,battle.deckid).Where(x=>x!=0).ToList();
            if(cards.Any(seq=>{var card=catalog.RequireCard(p.CardIds[(int)seq-1]);return link.EnterType!=BscTable.CARD_FACTION.FACTION_ALL&&card.Faction!=link.EnterType;}))return Invalid("card does not meet the original faction restriction");
            var waves=BoardResources.Waves[diff].Single(x=>x.Id==stage);var selected=waves.WaveList.GroupBy(x=>x.Id).OrderBy(x=>x.Key).Select(g=>WeightedLoot(g.ToArray(),x=>(int)Math.Max(1,Math.Round(x.Prob*1000)))).ToList();
            r.Stage=stage;r.Difficulty=diff;r.Cards=cards;r.ActiveStamp=NextLocalBattleStamp(p);LocalBattleAttempts.Bind(p,stage,r.ActiveStamp);RecordResourceObjective(p,stage,false);p.Stamina-=(uint)stamina;r.Used[stage]=r.Used.GetValueOrDefault(stage)+1;
            var result=new GsResourceStageBattleStartOk{stamina=p.Stamina,staminaStamp=p.StaminaStamp,timeStamp=r.ActiveStamp};result.waveProbIdList.AddRange(selected.Select(x=>(uint)x.ProbId));return ok(result);
        }
        if(request is GsResourceStageSkipRequest) {
            if(!r.Cleared.Any(x=>x/10==stage&&x%10>=diff))return fail("clear this resource difficulty before sweeping",ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE);
            var reward=BoardResources.Rewards[diff].Single(x=>x.StageId==stage);var plans=new List<ShopGrant>();var total=new ShopGrant();
            for(int i=0;i<count;i++){var grant=new ShopGrant();if(!ResourceReward(p,reward,false,grant))return Invalid("original resource reward unavailable");plans.Add(grant);total.Gold+=grant.Gold;total.Cash+=grant.Cash;total.Stamina+=grant.Stamina;foreach(var item in grant.Items)total.Items[item.Key]=total.Items.GetValueOrDefault(item.Key)+item.Value;total.Cards.AddRange(grant.Cards);}
            var growth=StageGrowth(p,(ulong)reward.RewardExp*count,(uint)stamina);if(growth==null||!CanPayAndGrant(p,total,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("resource sweep reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsResourceStageSkipOk{earnedExp=(uint)reward.RewardExp*count,deltaExpItemInfo=ItemInfo(210002001,0)};
            for(int i=0;i<plans.Count;i++){var earned=new PKBattleSkipReward{skipSequence=(uint)i+1,earnedCoinInfo=GrantCoins(plans[i])};ApplyShopGrant(p,plans[i],BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,earned.earnedItemList,earned.earnedCardList);result.earnedList.Add(earned);}
            p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;r.Used[stage]=r.Used.GetValueOrDefault(stage)+count;RecordResourceObjective(p,stage,false,count);RecordResourceObjective(p,stage,true,count);result.exp=p.Exp;result.level=p.Level;result.coinInfo=Coins(p);result.stamina=p.Stamina;result.staminaMax=catalog.StaminaMax(p.Level);result.staminaStamp=p.StaminaStamp;return ok(result);
        }
        return Invalid("unsupported resource operation");
    }
    private bool ResourceReward(LocalPlayer p,BscTable.ResourceStageRewardData reward,bool first,ShopGrant grant)
    {
        grant.Gold+=(uint)Math.Max(0,reward.Gold);grant.Cash+=(uint)Math.Max(0,reward.Gem);
        return RaidRewards(p,grant,reward.NpcRewardIdType,reward.NpcRewardId,reward.RewardQuantity,reward.DropProb)&&(!first||RaidRewards(p,grant,reward.FirstNpcRewardIdType,reward.FirstNpcRewardId,reward.FirstRewardQuantity,reward.FirstDropProb))&&(reward.CardRewardExp<=0||AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,210002001,reward.CardRewardExp));
    }
}
