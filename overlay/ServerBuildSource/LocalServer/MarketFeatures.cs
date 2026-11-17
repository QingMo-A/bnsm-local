using ProtoBuf;

// Optional field 5 extends the original single-purchase packet for local players.
// Original packets omit it and continue to buy one pack.
[ProtoContract]
internal sealed class LocalShopBuyRequest
{
    [ProtoMember(1,IsRequired=true)] public SessionKeyInfo keyInfo = new();
    [ProtoMember(2)] public uint shopId = 0;
    [ProtoMember(3)] public uint packageId = 0;
    [ProtoMember(4)] public uint SlotIndex = 0;
    [ProtoMember(5),System.ComponentModel.DefaultValue(1u)] public uint buyCount = 1;
}
internal sealed class LocalMarketSlot
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public uint GoodsId {get;set;}
    public uint Sold {get;set;}
    public uint Stock {get;set;}=1;
    public int Vip {get;set;}
    public int Discount {get;set;}
}
internal sealed class LocalMarketState
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public ulong NextRefresh {get;set;}
    public ulong Day {get;set;}
    public uint RefreshCount {get;set;}
    public int Tier {get;set;}
    public List<LocalMarketSlot> Slots {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    private static bool BossMarket(uint id)=>id is 91 or 92;
    private static BscTable.MONEY_TYPE MarketCurrency(uint id)=>id switch {1 or 92=>BscTable.MONEY_TYPE.MONEY_TYPE_CASH,3=>BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT,4=>BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT,_=>BscTable.MONEY_TYPE.MONEY_TYPE_GOLD};
    private static int MarketPrice(BscTable.GoodsData goods,uint id)=>MarketCurrency(id) switch {BscTable.MONEY_TYPE.MONEY_TYPE_CASH=>goods.Cash,BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT=>goods.BossP,BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT=>goods.ArenaP,_=>goods.Gold};
    private static BscTable.CONTENT_OPEN_TYPE MarketOpening(uint id)=>id switch {1=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_GNB_SHOP,3=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_ESSENCE,4=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_ARENA,_=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_GOLD};
    private static ulong MarketAnchor(BscTable.ShopSetData shop)
    {
        string t=shop.ResetTime.PadLeft(6,'0');
        return ulong.TryParse(t[..2],out ulong h)&&ulong.TryParse(t.Substring(2,2),out ulong m)&&ulong.TryParse(t.Substring(4,2),out ulong s)?h*3600+m*60+s:14400;
    }
    private BscTable.GoodsIdListInfo MarketTier(LocalPlayer p,uint id)
    {
        uint level=Math.Clamp(p.Level,1u,99u);
        return catalog.Shops.Single(x=>x.ShopId==id).GoodsIdList.First(x=>!x.IsFirst&&x.PcLvMin<=level&&level<=x.PcLvMax);
    }
    private uint MarketRefreshCost(LocalMarketState state)=>checked((uint)catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_MARKET_SHOP,checked((int)state.RefreshCount+1)).Value);
    private bool MarketItemEligible(BscTable.GoodsData goods,uint level)=>goods.ProductIdType==BscTable.GOODS_TYPE.GOODS_TYPE_ITEM
        &&goods.ProductId!=LocalStoryChapter.MaknaeFragmentId&&goods.Quantity>0&&goods.PcLevelMin<=level&&goods.PcLevelMax>=level
        &&catalog.ItemsById.ContainsKey(goods.ProductId);
    private static BscTable.GoodsData? RotateMarketGoods(IEnumerable<BscTable.GoodsData> pool,HashSet<int> previous,HashSet<int> selected,Random? random=null)
    {
        var choices=pool.Where(x=>!selected.Contains(x.ProductId)).GroupBy(x=>x.ProductId).ToArray();
        var fresh=choices.Where(x=>!previous.Contains(x.Key)).ToArray();
        if(fresh.Length>0)choices=fresh;
        if(choices.Length==0)return null;
        int Index(int count)=>random?.Next(count)??System.Security.Cryptography.RandomNumberGenerator.GetInt32(count);
        var group=choices[Index(choices.Length)].ToArray();
        return group[Index(group.Length)];
    }
    private List<LocalMarketSlot> RotateOriginalSlots(BscTable.GoodsIdListInfo tier,IReadOnlyList<LocalMarketSlot> oldSlots,
        IEnumerable<BscTable.GoodsData> pool)
    {
        var eligible=pool.ToArray();
        var byId=eligible.ToLookup(x=>x.GoodsId);
        var oldIds=oldSlots.Select(x=>(int)x.GoodsId).ToHashSet();
        var previous=eligible.Where(x=>oldIds.Contains(x.GoodsId)).Select(x=>x.ProductId).ToHashSet();
        var reserved=tier.GoodsId.Where(id=>!BscTable.GoodsIdListInfo.IsGroupGoods(id)).SelectMany(id=>byId[id]).Select(x=>x.ProductId).ToHashSet();
        var slots=new List<LocalMarketSlot>(tier.GoodsId.Count);
        for(int i=0;i<tier.GoodsId.Count;i++)
        {
            int id=tier.GoodsId[i];
            // Group weights are absent from the recovered catalog. Pick an
            // eligible original offer, giving each product an equal chance.
            var goods=BscTable.GoodsIdListInfo.IsGroupGoods(id)?RotateMarketGoods(eligible,previous,reserved):byId[id].FirstOrDefault();
            if(goods!=null)reserved.Add(goods.ProductId);
            slots.Add(new(){GoodsId=(uint)(goods?.GoodsId??0),Stock=goods==null?0u:1u,Vip=tier.VipLevel.ElementAtOrDefault(i),Discount=tier.DiscountRatio.ElementAtOrDefault(i)});
        }
        return slots;
    }
    private LocalMarketState OriginalMarket(LocalPlayer p,uint id,bool reroll=false)
    {
        var shop=catalog.Shops.Single(x=>x.ShopId==id);
        var tier=MarketTier(p,id);
        ulong local=Now+8*3600,anchor=MarketAnchor(shop),day=(local-anchor)/86400;
        if(!p.Markets.TryGetValue(id,out var state))p.Markets[id]=state=new();
        if(state.Day!=day){state.Day=day;state.RefreshCount=0;}
        if(!reroll&&state.NextRefresh>Now&&state.Tier==tier.PcLvMin&&state.Slots.Count==tier.GoodsId.Count&&
            !state.Slots.Any(slot=>catalog.Goods.Any(goods=>goods.GoodsId==slot.GoodsId&&goods.ProductId==LocalStoryChapter.MaknaeFragmentId)))return state;
        ulong period=(ulong)Math.Max(1,shop.ResetTerm)*3600;
        state.Slots=RotateOriginalSlots(tier,state.Slots,catalog.Goods.Where(g=>MarketPrice(g,id)>0&&MarketItemEligible(g,Math.Clamp(p.Level,1u,99u))));
        state.NextRefresh=((local-anchor)/period+1)*period+anchor-8*3600;
        state.Tier=tier.PcLvMin;
        return state;
    }
    private PKShopSetData OriginalMarketPage(LocalPlayer p,uint id)
    {
        if(id==5)return GuildMarketPage(p);
        if(id is not (1 or 2 or 3 or 4))return new(){shopId=id};
        var state=OriginalMarket(p,id);
        var result=new PKShopSetData{shopId=id,nextRegenTime=state.NextRefresh,nextRegenCost=MarketRefreshCost(state),regenCount=state.RefreshCount};
        result.goodsList.AddRange(state.Slots.Select(s=>new PKGoodsDataInfo{packageId=s.GoodsId,moneytype=MarketCurrency(id),maxStock=s.Stock,curStock=s.Sold}));
        return result;
    }
    private Reply BuyMarket(LocalPlayer p,LocalShopBuyRequest buy,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_ID)=>fail(why,code);
        if(buy.shopId==5)return GuildBuy(p,buy,ok,fail);
        if(buy.buyCount is 0 or >999 || (buy.shopId is not (1 or 2 or 3 or 4)&&!BossMarket(buy.shopId)))return Bad("invalid market or quantity");
        if(!BossMarket(buy.shopId)&&p.Level<catalog.ContentValue(MarketOpening(buy.shopId)))return Bad("original shop opening level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        BscTable.GoodsData? goods;LocalMarketSlot? slot=null;
        if(BossMarket(buy.shopId)){
            if (LocalCardRegistry.FindMarketPreview((int)buy.packageId) != null || LocalStoryChapter.IsMaknaeOffer((int)buy.packageId))
                return Bad("此奖励通过江湖篇章获取。", ServerErrorCode.ERROR_INVALID_BUY_PRODUCT_INFO);
            if(!FullShop.Goods.TryGetValue(buy.packageId,out goods))return Bad("unknown boss-shop goods");
            if(!p.ShopPages.TryGetValue(buy.shopId,out var page)||buy.SlotIndex>=page.Count||page[(int)buy.SlotIndex]!=buy.packageId)return Bad("boss-shop page changed",ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET);
        }else{
            var state=OriginalMarket(p,buy.shopId);
            if(buy.packageId==0||buy.SlotIndex>=state.Slots.Count||(slot=state.Slots[(int)buy.SlotIndex]).GoodsId!=buy.packageId)return Bad("original market refreshed",ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET);
            if(catalog.VipFor(p.VipExp).VipLv<slot.Vip)return Bad("original VIP slot requirement",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
            if(buy.buyCount>slot.Stock-slot.Sold)return Bad("market slot sold out",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            goods=catalog.Goods.SingleOrDefault(x=>x.GoodsId==buy.packageId);
        }
        if(goods==null||goods.ProductId==LocalStoryChapter.MaknaeFragmentId||goods.Quantity<=0||MarketPrice(goods,buy.shopId)<=0)return Bad("invalid original offer");
        var item=catalog.ItemsById.GetValueOrDefault(goods.ProductId);
        long quantity=(long)goods.Quantity*buy.buyCount;
        if(quantity>int.MaxValue||item==null||(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&quantity>50))return Bad("batch exceeds item or frame capacity");
        ulong unit=(ulong)MarketPrice(goods,buy.shopId)*(uint)(100-Math.Clamp(slot?.Discount??0,0,100))/100,cost=unit*buy.buyCount;
        var plan=new ShopGrant();
        if(!AddShopReward(p,plan,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,goods.ProductId,(int)quantity))return Bad("invalid purchase reward");
        var money=MarketCurrency(buy.shopId);
        ulong balance=money switch{BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT=>p.ArenaPoint,BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT=>p.WorldBossPoint,BscTable.MONEY_TYPE.MONEY_TYPE_CASH=>p.CashPoint,_=>p.Gold};
        if(balance<cost)return Bad("not enough market currency",money==BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT?ServerErrorCode.ERROR_NOT_ENOUGH_WORLD_BOSS_POINT:money==BscTable.MONEY_TYPE.MONEY_TYPE_CASH?ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT:money==BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT?ServerErrorCode.ERROR_NOT_ENOUGH_ARENA_POINT:ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        if(!CanPayAndGrant(p,plan,money,cost))return Bad("market reward exceeds storage capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsBuyIngameShopGoodsOk();ApplyShopGrant(p,plan,money,cost,result.deltaItemList,result.deltaCardList);
        if(slot!=null)slot.Sold+=buy.buyCount;
        result.nowCoinInfo=Coins(p);result.nowStamina=p.Stamina;result.staminaStamp=p.StaminaStamp;result.vipExp=p.VipExp;result.vipGrade=(uint)catalog.VipFor(p.VipExp).VipLv;
        result.shopSetData=ShopPage(p,buy.shopId);return ok(result);
    }
    private Reply RefreshMarket(LocalPlayer p,uint id,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(id==5)return GuildMarketRefresh(p,ok,fail);
        if(id is not (1 or 2 or 3 or 4))return fail("this shop does not use paid refreshes",ServerErrorCode.ERROR_INVALID_ID);
        if(!FeatureOpen(p,MarketOpening(id)))return fail("original shop opening level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var shop=catalog.Shops.Single(x=>x.ShopId==id);var state=OriginalMarket(p,id);
        uint cost=MarketRefreshCost(state);
        if(!shop.ResetActivate||state.RefreshCount>=catalog.VipFor(p.VipExp).ShopGoodsMaxResetCount)return fail("daily refresh limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(p.CashPoint<cost)return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        OriginalMarket(p,id,true);p.CashPoint-=cost;state.RefreshCount++;
        return ok(new GsResetIngameShopSellingListOk{nowCoinInfo=Coins(p),shopSetData=OriginalMarketPage(p,id)});
    }
}
