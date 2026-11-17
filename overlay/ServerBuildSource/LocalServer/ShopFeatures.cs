using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class ProtocolRouter
{
    private FullShopCatalog? fullShop;
    private FullShopCatalog FullShop => fullShop ??= new(catalog);
    private static PKUserItem EquipmentInfo(LocalPlayer p,LocalOwnedEquipment item)
    {
        var info=new PKUserItem {itemID=item.ItemId,itemSeq=item.Sequence,itemCount=1,cardSeq=EquipmentOwner(p,item.Sequence),registTime=item.RegisteredAt,currentEmpowerValue=item.CurrentEmpowerValue,bsBallId=item.BsBallId,bsBallCount=item.BsBallCount};
        info.empowerList.AddRange(Enumerable.Range(0,12).Select(i=>i<item.EmpowerValues.Count?item.EmpowerValues[i]:0));return info;
    }
    private static ulong ShopNextReset => ((Now+8*3600)/86400+1)*86400-8*3600;
    private static bool ShopMutation(string name) => name is "GS_EXCHANGE_GOLD_REQUEST" or "GS_BUY_VIP_REWARD_REQUEST" or "GS_BUY_INGAME_SHOP_GOODS_REQUEST" or "GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST" or "GS_BUY_SYSTEM_SHOP_GOODS_REQUEST" or "GS_SELL_USER_INVENTORY_ITEM_REQUEST";
    private Reply? HandleShopWithReceipt(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool mutation=ShopMutation(name);
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(mutation && p.ShopReceipts.TryGetValue(key,out var prior))
            return prior.Fingerprint==fingerprint ? new(prior.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name)
                : fail("shop sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var result=HandleShop(name,request,p,ok,fail);
        if(mutation && result?.Name.EndsWith("_OK")==true) {
            if(p.ShopReceipts.Count>=256)p.ShopReceipts.Remove(p.ShopReceipts.Keys.First());
            p.ShopReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        return result;
    }
    private sealed class ShopGrant
    {
        public Dictionary<uint,int> Items=[];
        public List<uint> Cards=[];
        public ulong Gold,Cash,Stamina,World,Tokens;
    }
    private bool AddShopReward(LocalPlayer p,ShopGrant grant,BscTable.REWARD_TYPE type,int id,int count)
    {
        if(count<=0)return false;
        switch(type) {
            case BscTable.REWARD_TYPE.REWARD_TYPE_ITEM:
                if(!catalog.ItemsById.ContainsKey(id))return false;
                if((long)grant.Items.GetValueOrDefault((uint)id)+count>int.MaxValue)return false;
                grant.Items[(uint)id]=grant.Items.GetValueOrDefault((uint)id)+count;return true;
            case BscTable.REWARD_TYPE.REWARD_TYPE_CARD:
                var card=catalog.UsableHeroes.FirstOrDefault(x=>x.Id==id);
                if(card==null)return false;
                for(int i=0;i<count;i++) {
                    bool owned=catalog.Cards.Any(x=>p.CardIds.Concat(grant.Cards).Contains((uint)x.Id) && HeroIdentity(x)==HeroIdentity(card));
                    if(owned) {if(card.PieceId<=0 || !AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,card.PieceId,GachaRuleSet.PieceCount(card.Talent)))return false;}
                    else grant.Cards.Add((uint)id);
                }
                return true;
            case BscTable.REWARD_TYPE.REWARD_TYPE_GUILD_TOKEN:grant.Tokens+=(uint)count;return true;
            case BscTable.REWARD_TYPE.REWARD_TYPE_GOLD:grant.Gold+=(uint)count;return true;
            case BscTable.REWARD_TYPE.REWARD_TYPE_CASH:grant.Cash+=(uint)count;return true;
            case BscTable.REWARD_TYPE.REWARD_TYPE_STAMINA:grant.Stamina+=(uint)count;return true;
            default:return false;
        }
    }
    private bool CanPayAndGrant(LocalPlayer p,ShopGrant plan,BscTable.MONEY_TYPE money,ulong cost)
    {
        bool cash=money==BscTable.MONEY_TYPE.MONEY_TYPE_CASH;
        bool token=money==BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN;
        bool world=money==BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT;bool arena=money==BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT;bool ranking=money==BscTable.MONEY_TYPE.MONEY_TYPE_RANKING_POINT;
        if(!cash && !token && !world && !arena && !ranking && money!=BscTable.MONEY_TYPE.MONEY_TYPE_GOLD)return false;
        if((cash?p.CashPoint:token?p.GuildTokens:world?p.WorldBossPoint:arena?p.ArenaPoint:ranking?p.RankingPoint:p.Gold)<cost)return false;
        int equipments=0;
        foreach(var pair in plan.Items) {
            var item=catalog.ItemsById[(int)pair.Key];
            if(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT)equipments=checked(equipments+pair.Value);
            else if((long)p.Inventory.GetValueOrDefault(pair.Key)+pair.Value>int.MaxValue)return false;
        }
        return (ulong)p.GuildTokens-(token?cost:0)+plan.Tokens<=uint.MaxValue && (ulong)p.WorldBossPoint-(world?cost:0)+plan.World<=uint.MaxValue && (ulong)p.CashPoint-(cash?cost:0)+plan.Cash<=uint.MaxValue && (ulong)p.Gold-(cash||token||world||arena||ranking?0:cost)+plan.Gold<=uint.MaxValue &&
            (ulong)p.Stamina+plan.Stamina<=uint.MaxValue && (plan.Cards.Count==0||p.CardIds.Count(x=>x!=0)+plan.Cards.Count<=300) &&
            (equipments==0||(long)p.EquipmentItems.Count+equipments<=30000) && p.NextEquipmentSequence<ulong.MaxValue-(ulong)equipments;
    }
    private void ApplyShopGrant(LocalPlayer p,ShopGrant plan,BscTable.MONEY_TYPE money,ulong cost,List<PKUserItem> items,List<PKUserCardInfo> cards)
    {
        if(money==BscTable.MONEY_TYPE.MONEY_TYPE_CASH)p.CashPoint-=(uint)cost;else if(money==BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN)p.GuildTokens-=(uint)cost;else if(money==BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT)p.WorldBossPoint-=(uint)cost;else if(money==BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT)p.ArenaPoint-=(uint)cost;else if(money==BscTable.MONEY_TYPE.MONEY_TYPE_RANKING_POINT)p.RankingPoint-=(uint)cost;else p.Gold-=(uint)cost;
        p.GuildTokens+=(uint)plan.Tokens;
        p.WorldBossPoint+=(uint)plan.World;
        p.Gold+=(uint)plan.Gold;p.CashPoint+=(uint)plan.Cash;p.Stamina+=(uint)plan.Stamina;
        if(plan.Stamina>0)p.StaminaStamp=Now;
        foreach(var pair in plan.Items) {
            var item=catalog.ItemsById[(int)pair.Key];
            if(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT) {
                for(int i=0;i<pair.Value;i++) {var owned=new LocalOwnedEquipment {ItemId=pair.Key,Sequence=p.NextEquipmentSequence++,RegisteredAt=Now};p.EquipmentItems.Add(owned);items.Add(EquipmentInfo(p,owned));}
            } else {p.Inventory[pair.Key]=p.Inventory.GetValueOrDefault(pair.Key)+pair.Value;items.Add(ItemInfo(pair.Key,pair.Value));}
        }
        foreach(uint id in plan.Cards) {p.CardIds.Add(id);p.CollectedCardIds.Add(id);cards.Add(CardInfo(id,(ulong)p.CardIds.Count));}
    }
    private PKShopSetData ShopPage(LocalPlayer p,uint id,IEnumerable<uint>? ids=null)
    {
        var result=new PKShopSetData {shopId=id,nextRegenTime=ShopNextReset,nextRegenCost=0};
        if(!BossMarket(id))return OriginalMarketPage(p,id);
        var chosen=ids?.ToList() ?? (p.ShopPages.TryGetValue(id,out var saved)?saved:FullShop.Goods.Keys.Take(FullShopCatalog.PageSize).ToList());
        p.ShopPages[id]=chosen;
        foreach(uint goods in chosen)result.goodsList.Add(new PKGoodsDataInfo {packageId=goods,moneytype=MarketCurrency(id),maxStock=1_000_000,curStock=0});
        return result;
    }
    private Reply? HandleShop(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_INVALID_ID);
        switch(name) {
            case "GS_GET_VIP_REWARD_PURCHASE_HISTORY_REQUEST":
                var history=new GsGetVipRewardPurchaseHistoryOk();history.packageId.AddRange(p.PurchasedVipPackages.Order());return ok(history);
            case "GS_BUY_VIP_REWARD_REQUEST":
                var vipBuy=(GsBuyVipRewardRequest)request!;
                var vip=catalog.Vip.FirstOrDefault(x=>x.LvUpRewardId==vipBuy.packageId && x.LvUpRewardId>0);
                var package=catalog.Packages.FirstOrDefault(x=>x.Id==vipBuy.packageId);
                if(vip==null || package==null || package.PackageType!=BscTable.PACKAGE_TYPE.VIP_LEVEL_UP_PACKAGE || package.Price<0)return Invalid("invalid original VIP package");
                if(catalog.VipFor(p.VipExp).VipLv<vip.VipLv)return fail("VIP level is too low",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
                if(p.PurchasedVipPackages.Contains(vipBuy.packageId))return fail("VIP package was already purchased",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                if(package.ItemId.Count!=package.ItemIdType.Count || package.ItemId.Count!=package.Quantity.Count)return Invalid("invalid original VIP rewards");
                var vipPlan=new ShopGrant();
                for(int i=0;i<package.ItemId.Count;i++)if(!AddShopReward(p,vipPlan,package.ItemIdType[i],package.ItemId[i],package.Quantity[i]))return Invalid("unsupported original VIP reward");
                if(!CanPayAndGrant(p,vipPlan,package.MoneyType,(ulong)package.Price))return fail("not enough currency or reward capacity",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
                var vipResult=new GsBuyVipRewardOk();ApplyShopGrant(p,vipPlan,package.MoneyType,(ulong)package.Price,vipResult.deltaItemList,vipResult.deltaCardList);
                p.PurchasedVipPackages.Add(vipBuy.packageId);vipResult.nowCoinInfo=Coins(p);return ok(vipResult);
            case "GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST":
                var query=((GsGetIngameShopSellingListRequest)request!).shopId;
                var selling=new GsGetIngameShopSellingListOk();
                if(query.Count>=2 && query[1]==FullShopCatalog.QueryMarker) {
                    if(!BossMarket(query[0]) || query.Count>FullShopCatalog.PageSize+2)return Invalid("invalid full-shop page");
                    var selected=query.Skip(2).ToArray();
                    if(selected.Distinct().Count()!=selected.Length || selected.Any(x=>!FullShop.Goods.ContainsKey(x)))return Invalid("unknown full-shop goods");
                    selling.shopSetDataList.Add(ShopPage(p,query[0],selected));
                } else {
                    if(query.Count>5 || query.Any(x=>x is <1 or >5))return Invalid("unknown market");
                    foreach(uint id in query.Distinct())selling.shopSetDataList.Add(ShopPage(p,id));
                }
                // Boss-shop pages are connection UI state, not an economic mutation.
                // Buying persists its validated page together with the purchase receipt.
                if(query.Count<2 || query[1]!=FullShopCatalog.QueryMarker)store.Save();return ok(selling);
            case "GS_BUY_INGAME_SHOP_GOODS_REQUEST":
                return BuyMarket(p,(LocalShopBuyRequest)request!,ok,fail);
            case "GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST":
                return RefreshMarket(p,((GsResetIngameShopSellingListRequest)request!).shopId,ok,fail);
            case "GS_GET_SYSTEM_SHOP_INFO_REQUEST":
                var info=new GsGetSystemShopInfoOk {nextRegenTime=ShopNextReset};
                info.SlotData.AddRange(catalog.SystemShops.Select(x=>new PKSystemShopSlotData {slotId=(uint)x.SlotId,BuyCount=p.SystemShopPurchases.GetValueOrDefault((uint)x.SlotId)}));return ok(info);
            case "GS_BUY_SYSTEM_SHOP_GOODS_REQUEST":
                return BuySystemShop(p,(GsBuySystemShopGoodsRequest)request!,ok,fail);
            case "GS_EXCHANGE_GOLD_REQUEST":
                return ExchangeGold(p,((GsExchangeGoldRequest)request!).continuousExchange,ok,fail);
            case "GS_EXCHANGE_GOLD_SIMULATE_REQUEST":
                uint trials=((GsExchangeGoldSimulateRequest)request!).exchangeCount;
                if(trials is 0 or >100)return Invalid("gold preview count must be 1..100");
                var simulated=JsonSerializer.Deserialize<LocalPlayer>(JsonSerializer.Serialize(p))!;
                var simulation=new GsExchangeGoldSimulateOk();
                for(int i=0;i<trials;i++) {var roll=RollGold(simulated,(int)simulated.GoldExchangeCount+1);simulated.GoldExchangeCount++;simulation.exchangeData.Add(new PKExchangeGoldSimulateData {exchangeCount=simulated.GoldExchangeCount,continueCriticalCount=(uint)simulated.GoldCriticalStreak,criticalAccumulateCount=(uint)simulated.GoldCriticalCount,criticalForbidCount=(uint)simulated.GoldCriticalForbid,exchangeAccumulateCount=(uint)simulated.GoldExchangeMisses,dropProb=roll.ExchangeGoldProb});}
                return ok(simulation);
            case "GS_SELL_USER_INVENTORY_ITEM_REQUEST":return SellShopItems(p,(GsSellUserItemRequest)request!,ok,fail);
            default:return null;
        }
    }
    private Reply BuySystemShop(LocalPlayer p,GsBuySystemShopGoodsRequest buy,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var slot=catalog.SystemShops.FirstOrDefault(x=>x.SlotId==buy.SlotId);var vip=catalog.VipFor(p.VipExp);
        if(slot==null || buy.buyTryCount is 0 or >1000)return fail("invalid system shop purchase",ServerErrorCode.ERROR_INVALID_ID);
        if(slot.OpenVVIPLevel>0 || slot.OpenVIPLevel>vip.VipLv)return fail("original VIP requirement",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
        var goods=catalog.Goods.FirstOrDefault(x=>x.GoodsId==slot.GoodsId);
        int limit=slot.BuyTimeVIPDataList.Where(x=>x.VipLevel<=vip.VipLv).OrderByDescending(x=>x.VipLevel).FirstOrDefault()?.BuyCount??0;
        uint used=p.SystemShopPurchases.GetValueOrDefault(buy.SlotId);
        if(goods==null || (ulong)used+buy.buyTryCount>(ulong)Math.Max(0,limit))return fail("daily system shop stock exhausted",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var prices=catalog.SystemShopPrices.FirstOrDefault(x=>x.ExpenseType==slot.RatioType)?.SystemShopExpenseWeightTryTimeDataList;
        if(prices==null || prices.Count==0)return fail("missing original system shop price",ServerErrorCode.ERROR_INVALID_ID);
        int basis=slot.MoneyType==BscTable.MONEY_TYPE.MONEY_TYPE_CASH?goods.Cash:goods.Gold;ulong cost=0;
        for(uint i=1;i<=buy.buyTryCount;i++) {var row=prices.Where(x=>x.TryTime<=used+i).OrderByDescending(x=>x.TryTime).FirstOrDefault()??prices[0];cost+=(ulong)Math.Ceiling(basis*(100L+row.Ratio)/100d);}
        var plan=new ShopGrant();
        var type=goods.ProductIdType switch {BscTable.GOODS_TYPE.GOODS_TYPE_ITEM=>BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,BscTable.GOODS_TYPE.GOODS_TYPE_CARD=>BscTable.REWARD_TYPE.REWARD_TYPE_CARD,_=>BscTable.REWARD_TYPE.REWARD_NONE};
        if(goods.Quantity<=0 || !AddShopReward(p,plan,type,goods.ProductId,checked(goods.Quantity*(int)buy.buyTryCount)) || !CanPayAndGrant(p,plan,slot.MoneyType,cost))return fail("not enough currency or invalid reward",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        var result=new GsBuySystemShopGoodsOk();ApplyShopGrant(p,plan,slot.MoneyType,cost,result.deltaItemList,result.deltaCardList);p.SystemShopPurchases[buy.SlotId]=used+buy.buyTryCount;result.nowCoinInfo=Coins(p);return ok(result);
    }
    private PKExchangeGoldData RollGold(LocalPlayer state,int attempt)
    {
        var row=catalog.CriticalGold.Where(x=>x.TryTime<=attempt).OrderByDescending(x=>x.TryTime).First();
        double probability=Math.Clamp(row.CriticalProb+100*catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.MOD_NORMAL_CRI_PROB)*state.GoldExchangeMisses,0,100);
        bool critical=state.GoldCriticalForbid==0 && RandomNumberGenerator.GetInt32(10000)<probability*100;
        uint multiplier=100;
        if(critical) {
            multiplier=(uint)RandomNumberGenerator.GetInt32(Math.Max(100,row.BonusProb_Min),Math.Max(100,row.BonusProb_Max)+1);
            state.GoldExchangeMisses=0;state.GoldCriticalCount++;state.GoldCriticalStreak++;
            int cap=(int)catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.CONSEC_CRI_ACCUM_NUM);
            if(cap>0 && state.GoldCriticalStreak>=cap) {state.GoldCriticalForbid=(int)catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.CONSEC_CRI_INHIBIT_NUM);state.GoldCriticalStreak=0;}
        } else {state.GoldExchangeMisses++;state.GoldCriticalStreak=0;if(state.GoldCriticalForbid>0)state.GoldCriticalForbid--;}
        ulong gold=(ulong)Math.Floor(row.ExchangeValue*multiplier/100d);
        return new PKExchangeGoldData {ExchangeGold=checked((uint)gold),ExchangeGoldProb=multiplier};
    }
    private Reply ExchangeGold(LocalPlayer p,bool continuous,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var vip=catalog.VipFor(p.VipExp);int remain=vip.ExchangeToGoldBonusCount-(int)p.GoldExchangeCount;
        if(remain<=0)return fail("daily gold exchange limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(continuous && !vip.ChainGoldExchange)return fail("chain exchange VIP requirement",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
        int count=continuous?Math.Min(10,remain):1;ulong cost=0;
        for(int i=1;i<=count;i++)cost+=(uint)catalog.CriticalGold.Where(x=>x.TryTime<=p.GoldExchangeCount+i).OrderByDescending(x=>x.TryTime).First().Cost;
        if(cost>p.CashPoint)return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        var state=new LocalPlayer {GoldExchangeMisses=p.GoldExchangeMisses,GoldCriticalCount=p.GoldCriticalCount,GoldCriticalForbid=p.GoldCriticalForbid,GoldCriticalStreak=p.GoldCriticalStreak};
        var result=new GsExchangeGoldOK();
        for(int i=1;i<=count;i++)result.ExchangeGoldData.Add(RollGold(state,(int)p.GoldExchangeCount+i));
        ulong gain=(ulong)result.ExchangeGoldData.Sum(x=>(long)x.ExchangeGold);
        if((ulong)p.Gold+gain>uint.MaxValue)return fail("gold capacity reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        p.CashPoint-=(uint)cost;p.Gold+=(uint)gain;p.GoldExchangeCount+=(uint)count;
        p.GoldExchangeMisses=state.GoldExchangeMisses;p.GoldCriticalCount=state.GoldCriticalCount;p.GoldCriticalStreak=state.GoldCriticalStreak;p.GoldCriticalForbid=state.GoldCriticalForbid;
        result.currentCoinInfo=Coins(p);return ok(result);
    }
    private Reply SellShopItems(LocalPlayer p,GsSellUserItemRequest sell,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(sell.sellItemInfo.Count is 0 or >128 || sell.sellItemInfo.GroupBy(x=>x.itemSeq==0?(ulong)x.itemID:x.itemSeq).Any(g=>g.Count()>1))return fail("invalid sale list",ServerErrorCode.ERROR_INVALID_ID);
        ulong gold=0,cash=0;var deltas=new List<PKUserItem>();var equipments=new List<LocalOwnedEquipment>();
        foreach(var request in sell.sellItemInfo) {
            var item=catalog.Items.FirstOrDefault(x=>x.Id==request.itemID);
            if(item==null || item.Sell<=0 || item.Value<=0 || request.itemCount<=0)return fail("item cannot be sold",ServerErrorCode.ERROR_INVALID_ID);
            if(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT) {
                var owned=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==request.itemSeq && x.ItemId==request.itemID);
                if(owned==null || request.itemCount!=1)return fail("equipment is not owned",ServerErrorCode.ERROR_INVALID_ID);
                if(owned.Locked)return fail("locked equipment cannot be sold",ServerErrorCode.ERROR_INVALID_ID);
                if(EquipmentOwner(p,owned.Sequence)!=0)return fail("unequip this item before selling it",ServerErrorCode.ERROR_INVALID_ID);
                equipments.Add(owned);var delta=EquipmentInfo(p,owned);delta.itemCount=-1;deltas.Add(delta);
            } else {
                if((request.itemSeq!=0 && request.itemSeq!=request.itemID) || p.Inventory.GetValueOrDefault(request.itemID)<request.itemCount)return fail("not enough owned items",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
                deltas.Add(ItemInfo(request.itemID,-request.itemCount));
            }
            if(item.SellMoneyType==BscTable.ITEM_SELL_MONEY_TYPE.SELL_MONEY_TYPE_GOLD)gold+=(ulong)item.Value*(uint)request.itemCount;
            else if(item.SellMoneyType==BscTable.ITEM_SELL_MONEY_TYPE.SELL_MONEY_TYPE_CASH)cash+=(ulong)item.Value*(uint)request.itemCount;
            else return fail("unsupported sale currency",ServerErrorCode.ERROR_INVALID_ID);
        }
        if((ulong)p.Gold+gold>uint.MaxValue || (ulong)p.CashPoint+cash>uint.MaxValue)return fail("currency capacity reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        foreach(var delta in deltas)if(!equipments.Any(x=>x.Sequence==delta.itemSeq))p.Inventory[delta.itemID]+=delta.itemCount;
        foreach(var item in equipments)p.EquipmentItems.Remove(item);
        p.Gold+=(uint)gold;p.CashPoint+=(uint)cash;var result=new GsSellUserItemOk {currentCoinInfo=Coins(p)};result.deltaItemList.AddRange(deltas);return ok(result);
    }
}
