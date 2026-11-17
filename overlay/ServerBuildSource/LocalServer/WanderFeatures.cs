using System.Text.Json;

internal sealed class LocalWanderCard
{
    public ulong Sequence {get;set;}
    public bool Dead {get;set;}
    public float Hp {get;set;}=1;
    public float Sp {get;set;}
    public uint Bubbles {get;set;}
    public PKWanderBattleUsedCardInfo Pack()=>new(){cardSeq=Sequence,death=Dead,hpRatio=Hp,spRatio=Sp,spBubbleCount=Bubbles};
}
internal sealed class LocalWanderState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public int Difficulty {get;set;}
    public uint Spot {get;set;}=1;
    public uint Buff {get;set;}
    public uint BuffRolls {get;set;}
    public uint RewardRolls {get;set;}
    public uint Points {get;set;}
    public uint BestPoints {get;set;}
    public List<uint> BestSpots {get;set;}=[0,0,0];
    public uint Resets {get;set;}
    public uint Bought {get;set;}
    public uint PaidLeft {get;set;}
    public ulong LastReset {get;set;}
    public bool Complete {get;set;}
    public HashSet<uint> Claimed {get;set;}=[];
    public Dictionary<ulong,LocalWanderCard> Cards {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    private void RefreshWander(LocalPlayer p)
    {
        var w=p.Wander;ulong day=TowerDay(Now);if(w.Day==0||day>w.Day){w.Day=day;w.Resets=0;w.Bought=0;store.Save();}
    }
    private uint WanderFreeLeft(LocalPlayer p)=>(uint)Math.Max(0,(long)catalog.VipFor(p.VipExp).WanderBattleEnterCount-p.Wander.Resets);
    // Native BscBuyTicketPopup subtracts freeResetCount from the VIP allowance:
    // despite its name, the wire field contains FREE RESETS USED, not remaining.
    private PKWanderBattleRecord WanderRecord(LocalPlayer p)
    {
        var w=p.Wander;var result=new PKWanderBattleRecord{continueFlag=w.Difficulty!=0,difficulty=(BscTable.WANDER_BATTLE_DIFFICULTY)w.Difficulty,wanderPoint=w.Points,spotId=w.Spot,buffId=w.Buff,buffShuffleCount=w.BuffRolls,rewardCount=w.RewardRolls,startBattleTime=p.Raids.ActiveMode==5?p.Raids.ActiveStamp:0,todayResetCount=w.Resets,todayBuyCount=w.Bought,latestResetCountTime=w.LastReset,complated=w.Complete,paidRemainCount=w.PaidLeft,freeResetCount=w.Resets,bestWanderPoint=w.BestPoints};result.bestSpotId.AddRange(w.BestSpots);return result;
    }
    private uint WanderMax(LocalPlayer p,int difficulty)=> (uint)Raids.Wander[difficulty].Where(x=>x.OpenId<=p.ClearHardStage).Select(x=>x.Id).DefaultIfEmpty(0).Max();
    private void AdvanceWander(LocalPlayer p)
    {
        var w=p.Wander;w.BestSpots[w.Difficulty-1]=Math.Max(w.BestSpots[w.Difficulty-1],w.Spot);w.BestPoints=Math.Max(w.BestPoints,w.Points);
        if(w.Spot>=Raids.Wander[w.Difficulty].Max(x=>x.Id))w.Complete=true;else w.Spot++;
        w.BuffRolls=0;w.RewardRolls=0;
    }
    private bool PlanWanderReward(LocalPlayer p,BscTable.WanderSpotData row,bool extra,ShopGrant grant)
    {
        if(!extra){grant.Gold+=(uint)row.Gold;grant.Cash+=(uint)row.Gem;}
        return extra?RaidRewards(p,grant,row.PlusRewardIdType,row.PlusRewardId,row.PlusRewardQuantity,row.PlusDropProb.Select(x=>(double)x).ToList()):RaidRewards(p,grant,row.NpcRewardIdType,row.NpcRewardId,row.RewardQuantity,row.DropProb.Select(x=>(double)x).ToList());
    }
    private Reply? HandleWander(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!name.Contains("WANDER_BATTLE",StringComparison.Ordinal)||name.Contains("RANKING"))return null;
        return FeatureReceipt(p,p.WanderReceipts,name,session,payload,()=>ExecuteWander(name,request,p,ok,fail),fail,name is not ("GS_GET_WANDER_BATTLE_RECORD_REQUEST" or "GS_GET_WANDER_BATTLE_USED_CARD_INFO_LIST_REQUEST" or "GS_GET_WANDER_BATTLE_POINT_REWARD_LIST_REQUEST"));
    }
    private Reply ExecuteWander(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var w=p.Wander;Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);
        if(name=="GS_GET_WANDER_BATTLE_RECORD_REQUEST"){AbandonWanderBattle(p);return ok(new GsGetWanderBattleRecordOk{wanderBattleRecord=WanderRecord(p)});}
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_WANDER_BATTLE))return fail("original wander unlock level not reached",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(name=="GS_GET_WANDER_BATTLE_USED_CARD_INFO_LIST_REQUEST"){var result=new GsGetWanderBattleUsedCardInfoListOk();result.usedBattleCardInfoList.AddRange(w.Cards.Values.Select(x=>x.Pack()));return ok(result);}
        if(name=="GS_GET_WANDER_BATTLE_POINT_REWARD_LIST_REQUEST"){var result=new GsGetWanderBattlePointRewardListOk();result.pointRewardInfoList.AddRange(w.Claimed.Order().Select(x=>new PKWanderBattlePointRewardInfo{pointRewardId=x}));return ok(result);}
        if(p.ActiveStage!=null&&name!="GS_WANDER_BATTLE_END_CONFIRM_REQUEST")return fail("finish the active battle first",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        if(name=="GS_GET_WANDER_BATTLE_POINT_REWARD_REQUEST"){
            uint id=((GsGetWanderBattlePointRewardRequest)request!).pointRewardId;var row=Raids.WanderRewards.SingleOrDefault(x=>x.Id==id);
            if(row==null||row.WanderPoint>w.BestPoints||w.Claimed.Contains(id))return Invalid("point reward is locked or already claimed");
            var grant=new ShopGrant{Gold=(uint)row.Gold,Cash=(uint)row.Gem,World=(uint)row.WorldBossPoint};
            if(!RaidRewards(p,grant,row.BattleSpotRewardIdType,row.BattleSpotRewardId,row.RewardQuantity,row.DropProb.Select(x=>(double)x).ToList())||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("wander reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsGetWanderBattlePointRewardOk{earnedCoinInfo=GrantCoins(grant)};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);w.Claimed.Add(id);result.coinInfo=Coins(p);return ok(result);
        }
        if(name=="GS_BUY_WANDER_BATTLE_VIP_RESET_REQUEST")return BuyWanderEntries(p,1,ok,fail,name);
        if(name=="GS_RESET_WANDER_BATTLE_REQUEST"){
            if(w.Difficulty==0)return Invalid("choose a difficulty before resetting");
            if(WanderFreeLeft(p)==0&&w.PaidLeft==0)return fail("no daily wander reset opportunities",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(WanderFreeLeft(p)>0)w.Resets++;else w.PaidLeft--;
            w.LastReset=Now;w.Difficulty=0;w.Spot=1;w.Points=0;w.Buff=0;w.BuffRolls=0;w.RewardRolls=0;w.Complete=false;w.Cards.Clear();return ok(new GsResetWanderBattleRequestOk{wanderBattleRecord=WanderRecord(p),coinInfo=Coins(p)});
        }
        if(request is GsEnterWanderBattleRequest enter){
            int difficulty=(int)enter.difficulty;if(!Raids.Wander.ContainsKey(difficulty))return Invalid("unknown wander difficulty");
            uint level=(uint)(difficulty==1?catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_WANDER_BATTLE):catalog.Numbers[difficulty==2?BscTable.NUMBER_TYPE.WANDER_OPEN_LEVEL_HARD:BscTable.NUMBER_TYPE.WANDER_OPEN_LEVEL_HERO]);
            // The original menu gates map entry by level. Adventure OpenId locks
            // the nodes inside that map, not the ability to view the journey.
            if(p.Level<level)return fail("wander difficulty level not reached",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(w.Difficulty!=0&&w.Difficulty!=difficulty)return Invalid("reset the journey before changing difficulty");
            w.Difficulty=difficulty;return ok(new GsEnterWanderBattleOk{wanderBattleRecord=WanderRecord(p),coinInfo=Coins(p)});
        }
        if(w.Difficulty==0||w.Complete)return Invalid("choose a difficulty or reset the completed journey");
        var spot=Raids.Wander[w.Difficulty].SingleOrDefault(x=>x.Id==w.Spot);
        if(spot==null||w.Spot>WanderMax(p,w.Difficulty))return fail("clear the required hard adventure stage first; requiredHardStage="+(spot?.OpenId??0),ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE);
        uint? requested=Field(request,"spotId") as uint?;if(requested.HasValue&&requested.Value!=w.Spot)return Invalid("request must target the current wander spot");
        if(name=="GS_CLEAR_WANDER_BATTLE_MAP_REQUEST"){
            if(w.RewardRolls>0)return Invalid("confirm the pending reward before sweeping");
            uint best=Math.Min(w.BestSpots[w.Difficulty-1],WanderMax(p,w.Difficulty));uint end=Raids.Wander[w.Difficulty].Where(x=>x.Type==BscTable.WanderSpotDataType.Type_Reward&&x.Id<=best).Select(x=>(uint)x.Id).DefaultIfEmpty(0u).Max();
            if(end<w.Spot)return Invalid("no previously cleared reward checkpoint to sweep");
            var grant=new ShopGrant();uint points=0;
            foreach(var row in Raids.Wander[w.Difficulty].Where(x=>x.Id>=w.Spot&&x.Id<=end)){
                if(row.Type==BscTable.WanderSpotDataType.Type_Reward&&!PlanWanderReward(p,row,false,grant))return Invalid("sweep reward unavailable");
                if(row.Type==BscTable.WanderSpotDataType.Type_Battle)points+=(uint)row.WanderPoint;
            }
            if((ulong)w.Points+points>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("sweep reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsClearWanderBattleMapOk{earnedCoinInfo=GrantCoins(grant)};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);w.Points+=points;w.Spot=end;w.Buff=0;AdvanceWander(p);result.wanderBattleRecord=WanderRecord(p);result.coinInfo=Coins(p);return ok(result);
        }
        if(name=="GS_SHUFFLE_WANDER_BATTLE_REWARD_REQUEST"){
            if(spot.Type!=BscTable.WanderSpotDataType.Type_Reward||w.RewardRolls>=2)return Invalid("this is not an available reward spot");
            bool extra=w.RewardRolls==1;uint cost=0;
            if(extra){var type=w.Difficulty==1?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_WANDER_BATTLE_PLUS_REWARD_NORMAL:w.Difficulty==2?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_WANDER_BATTLE_PLUS_REWARD_HARD:BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_WANDER_BATTLE_PLUS_REWARD_HERO;var fee=catalog.Expenses.FindItem(type,2,true);if(fee==null||fee.Value<0)return Invalid("original bonus reward fee missing");cost=(uint)fee.Value;}
            var grant=new ShopGrant();if(!PlanWanderReward(p,spot,extra,grant))return Invalid("original wander reward unavailable");
            if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_CASH,cost))return fail("not enough diamonds or reward storage",p.CashPoint<cost?ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT:ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsShuffleWanderBattleRewardOk{earnedCoinInfo=GrantCoins(grant)};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_CASH,cost,result.earnedItemList,result.earnedCardList);w.RewardRolls++;result.wanderBattleRecord=WanderRecord(p);result.coinInfo=Coins(p);return ok(result);
        }
        if(name=="GS_CONFIRM_WANDER_BATTLE_REWARD_REQUEST"){
            if(spot.Type!=BscTable.WanderSpotDataType.Type_Reward)return Invalid("collect the reward before continuing");AdvanceWander(p);return ok(new GsConfirmWanderBattleRewardOk{wanderBattleRecord=WanderRecord(p)});
        }
        if(name=="GS_SHUFFLE_WANDER_BATTLE_BUFF_REQUEST"){
            if(spot.Type!=BscTable.WanderSpotDataType.Type_Buff||w.BuffRolls>=100)return Invalid("not a buff spot or reroll limit reached");
            var set=Raids.BuffSets.SingleOrDefault(x=>x.Id==spot.BuffId);if(set==null||set.BuffDataList.Count==0)return Invalid("original buff set missing");
            var fee=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_WANDER_BATTLE_BUFF_SHUFFLE,(int)w.BuffRolls,true);
            ulong cost=w.BuffRolls==0?0:fee==null||fee.Value<0?ulong.MaxValue:(ulong)((float)(fee.Value*set.BaseCost)/100f);
            if(cost>p.Gold)return fail("not enough gold for buff reroll",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            var buffs=set.BuffDataList.Where(x=>Raids.Buffs.Any(b=>b.Id==x.BuffId)).ToArray();if(buffs.Length==0)return Invalid("no supported buff in original set");
            p.Gold-=(uint)cost;w.Buff=(uint)buffs[WeightedLoot(Enumerable.Range(0,buffs.Length).ToArray(),i=>Math.Max(0,buffs[i].Prob))].BuffId;w.BuffRolls++;return ok(new GsShuffleWanderBattleBuffOk{wanderBattleRecord=WanderRecord(p),coinInfo=Coins(p)});
        }
        if(request is GsConfirmWanderBattleBuffRequest buff){
            if(spot.Type!=BscTable.WanderSpotDataType.Type_Buff||w.BuffRolls==0||buff.buffId!=w.Buff)return Invalid("confirm the rolled buff");AdvanceWander(p);return ok(new GsConfirmWanderBattleBuffOk{wanderBattleRecord=WanderRecord(p)});
        }
        if(request is LocalWanderStartRequest start){
            if(spot.Type!=BscTable.WanderSpotDataType.Type_Battle||start.buffId!=w.Buff)return Invalid("invalid battle spot or buff");
            if(spot.ConsumeStamina>p.Stamina||spot.ConsumeGold>p.Gold||spot.ConsumeGem>p.CashPoint)return Invalid("not enough battle resources");
            if(spot.ConsumeSoulStone!=0)return Invalid("unsupported original soul stone entry cost");
            var main=(start.hasRoster?start.cards:DeckSlots(p,22)).Where(x=>x!=0).ToList();
            var reserves=start.reserves.Where(x=>x!=0).ToList();
            bool Available(ulong seq)=>Owns(p,seq)&&!InTraining(p,seq)&&w.Cards.GetValueOrDefault(seq)?.Dead!=true;
            if(main.Count>5||reserves.Count>45||main.Count+reserves.Count==0||main.Concat(reserves).Distinct().Count()!=main.Count+reserves.Count||main.Concat(reserves).Any(x=>!Available(x)))
                return fail("empty deck, dead card, missing card or training card; main="+string.Join(',',main)+"; reserves="+string.Join(',',reserves),ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            p.Raids.BattleCards=main;p.Raids.BattleSideCards=reserves;p.Raids.RosterSnapshot=start.hasRoster;
            p.Stamina-=(uint)spot.ConsumeStamina;p.Gold-=(uint)spot.ConsumeGold;p.CashPoint-=(uint)spot.ConsumeGem;
            var r=p.Raids;r.ActiveMode=5;r.ActiveBoss=w.Spot;r.ActiveStamp=NextLocalBattleStamp(p);LocalBattleAttempts.Bind(p,w.Spot,r.ActiveStamp);return ok(new GsWanderBattleStartOk{wanderBattleRecord=WanderRecord(p),startTimeStamp=r.ActiveStamp});
        }
        if(request is GsWanderBattleEndConfirmRequest endBattle){
            if(!RaidEndMatches(p,5,w.Spot,endBattle.startTimeStamp)||endBattle.buffId!=w.Buff||!Enum.IsDefined(endBattle.endType)||spot.Type!=BscTable.WanderSpotDataType.Type_Battle)return fail("no matching wander battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            // Native setup can promote a reserve into an empty main slot before
            // producing sideDeckCardSeq. Validate against the entry snapshot, not
            // against that shortened end-of-battle list.
            var side=(endBattle.battleEndSyncData?.sideDeckCardSeq??[]).Where(x=>x!=0).ToList();
            var used=(endBattle.battleEndSyncData?.usedSideDeckCardSeq??[]).Where(x=>x!=0).ToList();
            var roster=p.Raids.BattleCards.Concat(p.Raids.BattleSideCards).ToHashSet();
            if(!p.Raids.RosterSnapshot)roster.UnionWith(side); // Older clients lack entry reserves.
            string? invalid=null;
            if(side.Count>45||side.Distinct().Count()!=side.Count||side.Any(x=>!Owns(p,x)||p.Raids.RosterSnapshot&&!roster.Contains(x)))invalid="reserve identity";
            else if(used.Distinct().Count()!=used.Count||used.Any(x=>!roster.Contains(x)))invalid="used reserve identity";
            else if(endBattle.cardInfoList.Count==0||endBattle.cardInfoList.Count>roster.Count||endBattle.cardInfoList.Select(x=>x.cardSeq).Distinct().Count()!=endBattle.cardInfoList.Count)invalid="survivor count or duplicates";
            else foreach(var card in endBattle.cardInfoList) {
                if(!roster.Contains(card.cardSeq)||!Owns(p,card.cardSeq)||InTraining(p,card.cardSeq)||w.Cards.GetValueOrDefault(card.cardSeq)?.Dead==true){invalid="participant identity; seq="+card.cardSeq;break;}
                if(!float.IsFinite(card.hpRatio)||card.hpRatio<0||card.hpRatio>1||!float.IsFinite(card.spRatio)||card.spRatio<0||card.spRatio>100000||card.spBubbleCount>100){invalid="participant stats; seq="+card.cardSeq+"; hp="+card.hpRatio+"; sp="+card.spRatio+"; bubbles="+card.spBubbleCount;break;}
            }
            if(invalid!=null)return fail("invalid wander survivor or reserve roster: "+invalid+"; entry="+string.Join(',',roster)+"; reported="+string.Join(',',endBattle.cardInfoList.Select(x=>x.cardSeq))+"; side="+string.Join(',',side),ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            bool won=endBattle.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS;uint offset=(uint)(endBattle.startTimeStamp%1000*w.Spot);if(endBattle.enemyAliveCount<offset||won&&endBattle.enemyAliveCount!=offset)return Invalid("inconsistent wander outcome");
            var grant=new ShopGrant();if(won&&!PlanWanderReward(p,spot,false,grant))return Invalid("wander battle reward unavailable");
            if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0)||(ulong)w.Points+(won?(uint)spot.WanderPoint:0)>uint.MaxValue)return fail("wander reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsWanderBattleEndConfirmOk{earnedCoinInfo=GrantCoins(grant),battleEndSyncData=new()};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);
            foreach(var card in endBattle.cardInfoList)w.Cards[card.cardSeq]=new(){Sequence=card.cardSeq,Dead=card.death||card.hpRatio<=0,Hp=card.hpRatio,Sp=card.spRatio,Bubbles=card.spBubbleCount};
            w.Buff=0;if(won){w.Points+=(uint)spot.WanderPoint;AdvanceWander(p);}ClearRaidBattle(p);result.coinInfo=Coins(p);result.wanderBattleRecord=WanderRecord(p);result.usedBattleCardInfoList.AddRange(w.Cards.Values.Select(x=>x.Pack()));return ok(result);
        }
        return Invalid("unknown wander operation");
    }
    private Reply BuyWanderEntries(LocalPlayer p,uint count,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail,string name)
    {
        var w=p.Wander;uint max=(uint)Math.Max(0,catalog.VipFor(p.VipExp).WanderBattleEnterBonusCount);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_WANDER_BATTLE)||count==0||(ulong)w.Bought+count>max||(ulong)w.PaidLeft+count>uint.MaxValue)return fail("invalid wander reset ticket purchase",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var price=Raids.Prices.Single(x=>(int)x.VipRiceDiscountType==12).PriceDiscountBuyCountInfoList.SingleOrDefault(x=>x.Count==count);
        if(price==null||price.Price<0||p.CashPoint<price.Price)return fail("not enough diamonds for wander reset tickets",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        p.CashPoint-=(uint)price.Price;w.Bought+=count;w.PaidLeft+=count;
        return name=="GS_BUY_BONUS_COUNT_REQUEST"?ok(new GsBuyBonusCountOk{coinInfo=Coins(p),todayBuyCount=w.Bought,changedValue=w.PaidLeft,priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)12}):ok(new GsBuyWanderBattleVipResetOk{wanderBattleRecord=WanderRecord(p),coinInfo=Coins(p)});
    }
}
