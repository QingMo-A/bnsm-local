internal sealed partial class ProtocolRouter
{
    private LocalMarketState GuildMarket(LocalPlayer p,bool reroll=false)
    {
        ulong day=TowerDay(Now);var guild=MyGuild(p)!;
        if(!p.Markets.TryGetValue(5,out var state))p.Markets[5]=state=new();
        if(state.Day!=day){state.Day=day;state.RefreshCount=0;}
        if(!reroll&&state.NextRefresh>Now&&state.Slots.Count==8&&state.Tier==guild.Level)return state;
        var tier=catalog.Shops.Single(x=>x.ShopId==5).GoodsIdList.Single();
        // The original shop references goods groups absent from the recovered
        // tables. As in the gold shop, select distinct real item offers locally.
        var eligible=catalog.Goods.Where(x=>x.GuildT>0&&!x.GuildPresent&&MarketItemEligible(x,p.Level)
            &&(x.GuildLevelMin==0||x.GuildLevelMin<=guild.Level)&&(x.GuildLevelMax==0||x.GuildLevelMax>=guild.Level));
        state.Slots=RotateOriginalSlots(tier,state.Slots,eligible);
        state.Tier=(int)guild.Level;state.NextRefresh=(day+1)*86400-4*3600;
        return state;
    }
    private PKShopSetData GuildMarketPage(LocalPlayer p)
    {
        if(MyGuild(p)==null)return new(){shopId=5};
        var state=GuildMarket(p);var result=new PKShopSetData{shopId=5,nextRegenTime=state.NextRefresh,nextRegenCost=MarketRefreshCost(state),regenCount=state.RefreshCount};
        result.goodsList.AddRange(state.Slots.Select(x=>new PKGoodsDataInfo{packageId=x.GoodsId,moneytype=BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN,maxStock=x.Stock,curStock=x.Sold}));return result;
    }
    private Reply GuildBuy(LocalPlayer p,LocalShopBuyRequest buy,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string text,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_ID)=>fail(text,code);
        var g=MyGuild(p);if(g==null)return Bad("not a guild member",ServerErrorCode.ERROR_NOT_GUILD_USER);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_GUILD))return Bad("original guild-shop opening level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var state=GuildMarket(p);if(buy.SlotIndex>=state.Slots.Count)return Bad("invalid guild-shop slot");var slot=state.Slots[(int)buy.SlotIndex];
        if(slot.GoodsId!=buy.packageId)return Bad("guild shop refreshed",ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET);
        if(g.Level<slot.Vip)return Bad("guild level too low",ServerErrorCode.ERROR_INVALID_GUILD_INFO);
        if(buy.buyCount==0||buy.buyCount>slot.Stock-slot.Sold)return Bad("guild-shop slot sold out",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var goods=catalog.Goods.Single(x=>x.GoodsId==slot.GoodsId);var plan=new ShopGrant();
        ulong cost=(ulong)(int)(goods.GuildT*(double)(100-slot.Discount)*0.01)*buy.buyCount;
        if(goods.ProductId==LocalStoryChapter.MaknaeFragmentId||!AddShopReward(p,plan,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,goods.ProductId,checked(goods.Quantity*(int)buy.buyCount))||!CanPayAndGrant(p,plan,BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN,cost))return Bad("not enough guild tokens or reward capacity",ServerErrorCode.ERROR_NOT_ENOUGH_GUILD_TOKEN);
        var result=new GsBuyIngameShopGoodsOk();ApplyShopGrant(p,plan,BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN,cost,result.deltaItemList,result.deltaCardList);slot.Sold+=buy.buyCount;
        result.nowCoinInfo=Coins(p);result.nowStamina=p.Stamina;result.staminaStamp=p.StaminaStamp;result.vipExp=p.VipExp;result.vipGrade=(uint)catalog.VipFor(p.VipExp).VipLv;result.shopSetData=GuildMarketPage(p);return ok(result);
    }
    private Reply GuildMarketRefresh(LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(MyGuild(p)==null)return fail("not a guild member",ServerErrorCode.ERROR_NOT_GUILD_USER);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_GUILD))return fail("original guild-shop opening level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var state=GuildMarket(p);uint cost=MarketRefreshCost(state);
        if(state.RefreshCount>=catalog.VipFor(p.VipExp).ShopGoodsMaxResetCount)return fail("daily guild-shop refresh limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(p.CashPoint<cost)return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        GuildMarket(p,true);p.CashPoint-=cost;state.RefreshCount++;return ok(new GsResetIngameShopSellingListOk{nowCoinInfo=Coins(p),shopSetData=GuildMarketPage(p)});
    }
}
