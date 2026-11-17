internal sealed partial class ProtocolRouter
{
    private static uint CompetitiveBuyMode(BscTable.VIP_PRICE_DISCOUNT_TYPE kind)=>(int)kind switch{1=>10,2=>12,7=>31,14=>42,_=>0};
    private Reply? CompetitiveExtras(object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(request is GsBuyBonusCountRequest buy) {
            uint mode=CompetitiveBuyMode(buy.priceType);var v=catalog.VipFor(p.VipExp);var c=p.Competitive;
            uint count=buy.buyCount;if(count==0||p.ActiveStage!=null)return Bad("invalid ticket count or battle active");
            uint bought,maximum;
            if(mode!=0){bought=PvpRecord(p,mode).Bought;maximum=(uint)Math.Max(0,mode==10?v.RankingPvpEnterBonusCount:mode==42?v.BalancePvpEnterBonusCount:mode==31?v.InfiniteArenaPvpEnterBonusCount:v.ArenaPvpEnterBonusCount);}
            else if((int)buy.priceType==16){bought=c.WorldReviveBought;maximum=(uint)Math.Max(0,v.WorldWarBonusRecoveryCount);}
            else if((int)buy.priceType==5){bought=c.CastleExchangeBought;maximum=(uint)Math.Max(0,v.WealthPointExchangeBonusCount);}
            else if((int)buy.priceType==4){bought=c.CastleBought;maximum=(uint)Math.Max(0,v.CastleAttackBonusCount);}
            else return null;
            if((ulong)bought+count>maximum)return Bad("original daily ticket purchase limit");
            var price=Raids.Prices.Single(x=>x.VipRiceDiscountType==buy.priceType).PriceDiscountBuyCountInfoList.SingleOrDefault(x=>x.Count==count);
            if(price==null||price.Price<0||p.CashPoint<price.Price)return fail("not enough ticket diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)price.Price;uint left;
            if(mode!=0){var r=PvpRecord(p,mode);r.Bought+=count;r.PaidLeft+=count;left=r.PaidLeft;}
            else if((int)buy.priceType==16){c.WorldReviveBought+=count;left=WorldUserInfo(p).revivalRemainCount;}
            else if((int)buy.priceType==5){c.CastleExchangeBought+=count;c.CastleExchangePaidLeft+=count;left=c.CastleExchangePaidLeft;}
            else{c.CastleBought+=count;c.CastlePaidLeft+=count;left=c.CastlePaidLeft;}
            return ok(new GsBuyBonusCountOk{coinInfo=Coins(p),todayBuyCount=bought+count,changedValue=left,priceType=buy.priceType});
        }
        if(request is GsGetInfinitArenaTodayRewardRequest reward) {
            var record=PvpRecord(p,31);var row=Competitive.Rewards.FirstOrDefault(x=>x.Id==reward.rewardId&&x.PvpBattleType==PvpType(31)&&x.BattleResultType==BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_SCORE);
            if(row==null||row.Score>record.RewardPoints||record.RewardClaims.Contains(reward.rewardId))return Bad("score reward unavailable or already claimed");
            uint exp=p.Exp,gold=p.Gold,cash=p.CashPoint,arena=p.ArenaPoint;
            if(!GrantCompetitiveRewards(p,row.RewardValueIdType,row.RewardValueId,row.ItemQuantity,out var items,out var cards))return Bad("reward capacity exceeded");record.RewardClaims.Add(reward.rewardId);
            var result=new GsGetInfinitArenaTodayRewardOk{todayRewardPoint=record.RewardPoints,coinInfo=Coins(p),earnedCoinInfo=new(){gold=p.Gold-gold,cashPoint=p.CashPoint-cash,arenaPoint=p.ArenaPoint-arena},exp=p.Exp,earnedExp=p.Exp-exp};result.earnedItemList.AddRange(items);result.earnedCardList.AddRange(cards);return ok(result);
        }
        if(request is GsResetPvpBattleCoolTimeRequest reset) {
            uint mode=(int)reset.pvpBattleType==3?12u:(int)reset.pvpBattleType==9?31u:0;
            if(mode==0)return Bad("unsupported competitive cooldown");var record=PvpRecord(p,mode);var v=catalog.VipFor(p.VipExp);
            if(record.Latest==0||p.ActiveStage!=null||!(mode==12?v.ArenaPvPCooltimeResetCount:v.InfiniteArenaPvpCoolTimeResetCount))return Bad("no cooldown reset available");
            var row=catalog.Expenses.FindItem(mode==12?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_ARENA_COOLTIME:BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_INFINITE_ARENA_COOLTIME,checked((int)record.CoolResets+1));
            if(row.Value<0||p.CashPoint<row.Value)return fail("not enough cooldown diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);p.CashPoint-=(uint)row.Value;record.CoolResets++;record.Latest=0;return ok(new GsResetPvpBattleCoolTimeOk{coinInfo=Coins(p)});
        }
        return null;
    }
}
