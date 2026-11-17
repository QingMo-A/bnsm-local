internal sealed partial class ProtocolRouter
{
    private static int CastlePrice(BscTable.GoodsData goods,BscTable.MONEY_TYPE money)=>money switch
    {
        BscTable.MONEY_TYPE.MONEY_TYPE_GOLD=>goods.Gold,
        BscTable.MONEY_TYPE.MONEY_TYPE_CASH=>goods.Cash,
        BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT=>goods.ArenaP,
        _=>-1
    };
    private void RotateCastleStore(LocalPlayer p,LocalCastleSpot spot,BscTable.CastleSpotInfo row,bool refresh=false)
    {
        if(!refresh&&spot.StoreGoods.Count==row.GoodsId.Count)return;
        var oldIds=spot.StoreGoods.ToHashSet();
        var previous=catalog.Goods.Where(x=>oldIds.Contains((uint)x.GoodsId)).Select(x=>x.ProductId).ToHashSet();
        var selected=new HashSet<int>();
        var next=new List<uint>(row.GoodsId.Count);
        uint rotation=refresh?spot.StoreRotation+1:spot.StoreRotation;
        var rng=new Random(unchecked((int)(p.Uid*2654435761u+spot.Map*668265263u+spot.Spot*374761393u+rotation*2246822519u)));
        for(int i=0;i<row.GoodsId.Count;i++)
        {
            int id=row.GoodsId[i];
            var money=row.MoneyType[i];
            var pool=catalog.Goods.Where(x=>MarketItemEligible(x,p.Level)&&CastlePrice(x,money)>0);
            var goods=pool.FirstOrDefault(x=>x.GoodsId==id);
            if(goods==null&&BscTable.GoodsIdListInfo.IsGroupGoods(id))
                goods=RotateMarketGoods(pool,previous,selected,rng);
            if(goods!=null)selected.Add(goods.ProductId);
            next.Add((uint)(goods?.GoodsId??0));
        }
        spot.StoreGoods=next;
        spot.StoreRotation=rotation;
        if(refresh)spot.Purchases.Clear();
    }
    private Reply? CastleSupport(object? request,LocalPlayer p,LocalCastleSpot? spot,BscTable.CastleSpotInfo? row,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);var c=p.Competitive;
        if(request is GsBuyCastleBattleDeadCardRecoveryRequest) {
            if(p.ActiveStage!=null||c.CastleDeadCards.Count==0)return Bad("no castle cards need recovery or a battle is active");
            var cost=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_CASTLE_BATTLE_DEAD_CARD_RECOVERY,checked((int)c.CastleRecoveries+1));
            if(cost.Value<0||p.CashPoint<cost.Value)return fail("not enough castle recovery diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)cost.Value;c.CastleRecoveries++;c.CastleDeadCards.Clear();return ok(new GsBuyCastleBattleDeadCardRecoveryOk{coinInfo=Coins(p),todayDeadCardRecoveryCount=c.CastleRecoveries});
        }
        if(request is not (GsBuyCastleStoreGoodsRequest or GsResetCastleStoreRequest))return null;
        if(row==null||spot==null||spot.Status!=1||p.ActiveStage!=null)return Bad("castle shop requires an owned occupied castle outside battle");
        if(request is GsResetCastleStoreRequest) {
            if(spot.StoreReset>=catalog.VipFor(p.VipExp).CastleShopResetCount)return fail("castle shop reset limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var price=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_CASTLE_SHOP,checked((int)spot.StoreReset+1));
            if(price.Value<0||p.CashPoint<price.Value)return fail("not enough castle shop diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            RotateCastleStore(p,spot,row);
            RotateCastleStore(p,spot,row,true);
            p.CashPoint-=(uint)price.Value;spot.StoreReset++;
            var result=new GsResetCastleStoreOk{nowCoinInfo=Coins(p),resetCount=spot.StoreReset};result.storeInfoList.AddRange(CastleSpotInfo(p,spot).storeInfoList);return ok(result);
        }
        var buy=(GsBuyCastleStoreGoodsRequest)request;int index=(int)buy.SlotIndex;
        RotateCastleStore(p,spot,row);
        if(index<0||index>=row.GoodsId.Count||buy.goodsId==0||spot.StoreGoods[index]!=buy.goodsId)
            return fail("castle store offer changed",ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET);
        if(spot.Purchases.GetValueOrDefault(buy.SlotIndex)>0)return fail("castle store offer sold out",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(index<row.activation_upgrade.Count&&row.activation_upgrade[index]>(spot.Upgraded?1:0)||index<row.activation_vip.Count&&row.activation_vip[index]>catalog.VipFor(p.VipExp).VipLv)return Bad("castle store slot is locked");
        var goods=catalog.Goods.FirstOrDefault(x=>x.GoodsId==buy.goodsId);if(goods==null||goods.Quantity<=0)return Bad("unknown castle shop goods");
        var money=row.MoneyType[index];int basis=CastlePrice(goods,money);
        if(basis<0||row.MoneyFactor[index]<0)return Bad("invalid castle shop price");
        int discount=100-(int)Math.Round((float)row.MoneyFactor[index]*100f);
        ulong priceValue=(ulong)basis*(uint)(discount is >0 and <=100?100-discount:100)/100;
        var grant=new ShopGrant();
        var type=goods.ProductIdType==BscTable.GOODS_TYPE.GOODS_TYPE_CARD?BscTable.REWARD_TYPE.REWARD_TYPE_CARD:BscTable.REWARD_TYPE.REWARD_TYPE_ITEM;
        if(!AddShopReward(p,grant,type,goods.ProductId,goods.Quantity)||!CanPayAndGrant(p,grant,money,priceValue))return Bad("not enough castle currency or reward capacity");
        var purchased=new GsBuyCastleStoreGoodsOk{SlotIndex=buy.SlotIndex};ApplyShopGrant(p,grant,money,priceValue,purchased.deltaItemList,purchased.deltaCardList);spot.Purchases[buy.SlotIndex]=1;purchased.nowCoinInfo=Coins(p);purchased.castleStoreInfo=CastleSpotInfo(p,spot).storeInfoList[index];return ok(purchased);
    }
}
