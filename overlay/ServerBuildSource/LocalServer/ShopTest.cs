using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ShopTest
{
    public static async Task Run(string root)
    {
        string production=Path.Combine(root,"LocalServer/saves/players.json");
        byte[]? beforeProduction=File.Exists(production)?SHA256.HashData(File.ReadAllBytes(production)):null;
        string save=Path.Combine(root,"verification/shop",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Assert(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        T Read<T>(byte[] payload)=>(T)WireCodec.Deserialize(typeof(T),payload)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);
        var catalog=server.Catalog;var full=new FullShopCatalog(catalog);
        Assert(full.Goods.Count==catalog.Items.Count && full.Goods.Count==9437,"Every one of 9437 same-version items is listed exactly once");
        Assert(full.Goods.Values.All(x=>x.Gold>0 && x.Cash>0 && x.Quantity==1),"All catalogue offers have positive authoritative unit prices");
        Assert(full.Goods.Values.Select(x=>x.ProductId).Distinct().Count()==9437,"No original item is omitted or duplicated");
        async Task<(ushort Command,byte[] Payload)> Send(TcpClient socket,GameServerCommand command,object request) {
            await socket.GetStream().WriteAsync(codec.Encode((ushort)command,WireCodec.Serialize(request),true));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var raw=await WireCodec.ReadFrame(socket.GetStream(),timeout.Token);
            if(raw==null)throw new IOException("Server closed before replying to "+command);
            var response=codec.Decode(raw,true);return(response.Command,response.Payload);
        }
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        async Task<byte[]> Login(LoginServerCommand command,object request) {
            await login.GetStream().WriteAsync(codec.Encode((ushort)command,WireCodec.Serialize(request),true));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));return codec.Decode((await WireCodec.ReadFrame(login.GetStream(),timeout.Token))!,true).Payload;
        }
        await Login(LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="shop-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var started=Read<LsGameStartOk>(await Login(LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true}));
        var key=new SessionKeyInfo {uid=started.uid,loginSessionKey=started.loginSessionKey};
        var player=server.Store.Find(started.uid)!;player.Gold=1_000_000_000;player.CashPoint=1_000_000_000;
        using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);
        async Task<byte[]> Request(GameServerCommand command,object request,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;
            var response=await Send(game,command,request);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)response.Command).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(response.Payload).message);
            return response.Payload;
        }
        async Task<GsGetIngameShopSellingListOk> Page(uint shop,IEnumerable<uint>? goods=null) {
            var query=new GsGetIngameShopSellingListRequest {keyInfo=key};query.shopId.Add(shop);
            if(goods!=null) {query.shopId.Add(FullShopCatalog.QueryMarker);query.shopId.AddRange(goods);}
            return Read<GsGetIngameShopSellingListOk>(await Request(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,query));
        }
        async Task<GsBuyIngameShopGoodsOk> Buy(uint shop,uint goods,uint slot=0,bool retry=false) => Read<GsBuyIngameShopGoodsOk>(await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new GsBuyIngameShopGoodsRequest {keyInfo=key,shopId=shop,packageId=goods,SlotIndex=slot},retry:retry));
        async Task<GsGetExchangeInfoOk> ExchangeInfo()=>Read<GsGetExchangeInfoOk>(await Request(GameServerCommand.GS_GET_EXCHANGE_INFO_REQUEST,new GsGetExchangeInfoRequest {keyInfo=key}));

        // Enumerate the entire catalogue over the real encrypted wire in bounded pages.
        string clientCatalog=Path.Combine(root,"UnityRebuilt/ExportedProject/Assets/StreamingAssets/LocalShopGoods.bytes");
        if(!File.Exists(clientCatalog))clientCatalog="I:/BnsmModernWork/Project/Assets/StreamingAssets/LocalShopGoods.bytes";
        using (var source = File.OpenRead(clientCatalog)) {
            var exported = ProtoBuf.Serializer.Deserialize<BscTable.GoodsDataTable>(source).List;
            Assert(exported.Count==9437 && exported.All(x=>full.Goods[(uint)x.GoodsId].Gold==x.Gold && full.Goods[(uint)x.GoodsId].Cash==x.Cash && full.Goods[(uint)x.GoodsId].ProductId==x.ProductId), "Client catalogue uses exactly the server's item IDs and prices");
        }
        var known=full.Goods[FullShopCatalog.GoodsBase+260001001];
        Assert(known.Gold==4600 && known.Cash==5, "A known original material retains its 4600-gold / 5-diamond unit prices");
        var listed=new HashSet<uint>();bool framesFit=true;
        foreach(var batch in full.Goods.Keys.Chunk(FullShopCatalog.PageSize)) {
            var result=await Page(91,batch);var rows=result.shopSetDataList.Single().goodsList;
            foreach(var row in rows)listed.Add(row.packageId);
            framesFit &= rows.Count==batch.Length && WireCodec.Serialize(result).Length<7000 && rows.All(x=>x.moneytype==BscTable.MONEY_TYPE.MONEY_TYPE_GOLD);
        }
        Assert(listed.SetEquals(full.Goods.Keys),"All 9437 offers can be enumerated through actual encrypted protocol pages");
        Assert(framesFit,"Every catalogue page fits the original 8192-byte receive cap");
        var empty=await Page(92,Array.Empty<uint>());Assert(empty.shopSetDataList.Single().goodsList.Count==0,"Empty search results remain a valid shop page");
        var oversized=new GsGetIngameShopSellingListRequest {keyInfo=key};oversized.shopId.AddRange(new[]{91u,FullShopCatalog.QueryMarker});oversized.shopId.AddRange(full.Goods.Keys.Take(49));
        await Request(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,oversized,false);checks.Add("Oversized catalogue pages are rejected");

        // One representative of every original item type; exact backend prices and deltas.
        foreach(var type in catalog.Items.GroupBy(x=>x.ItemType)) {
            var item=type.First();uint goods=FullShopCatalog.GoodsBase+(uint)item.Id;await Page(91,new[]{goods});
            uint gold=player.Gold;int stack=player.Inventory.GetValueOrDefault((uint)item.Id),equipment=player.EquipmentItems.Count;
            var bought=await Buy(91,goods);
            bool received=item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT?player.EquipmentItems.Count==equipment+1:player.Inventory.GetValueOrDefault((uint)item.Id)==stack+1;
            Assert(received && bought.deltaItemList.Sum(x=>x.itemCount)==1 && player.Gold==gold-full.Goods[goods].Gold,"Purchase and exact gold charge: "+type.Key);
        }
        uint equipmentGoods=full.Goods.First(x=>catalog.Items.First(i=>i.Id==x.Value.ProductId).ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).Key;
        await Page(92,new[]{equipmentGoods});var equipmentBuy=await Buy(92,equipmentGoods);ulong equipmentSequence=equipmentBuy.deltaItemList.Single().itemSeq;
        int beforeReplayEquipment=player.EquipmentItems.Count;uint beforeReplayCash=player.CashPoint;
        var replay=await Buy(92,equipmentGoods,retry:true);
        Assert(player.EquipmentItems.Count==beforeReplayEquipment && player.CashPoint==beforeReplayCash && replay.deltaItemList.Single().itemSeq==equipmentSequence,"Shop retransmission cannot double-charge or duplicate an equipment instance");
        await Buy(92,equipmentGoods);
        var copies=player.EquipmentItems.Where(x=>x.ItemId==full.Goods[equipmentGoods].ProductId).ToList();
        Assert(copies.Count>=3 && copies.Select(x=>x.Sequence).Distinct().Count()==copies.Count,"Repeated equipment purchases retain independent stable serial numbers");
        uint goldSnapshot=player.Gold,cashSnapshot=player.CashPoint;
        await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new GsBuyIngameShopGoodsRequest {keyInfo=key,shopId=92,packageId=equipmentGoods,SlotIndex=1},false);
        Assert(player.Gold==goldSnapshot && player.CashPoint==cashSnapshot,"Mismatched page slots cannot charge currency");
        player.CashPoint=0;
        await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new GsBuyIngameShopGoodsRequest {keyInfo=key,shopId=92,packageId=equipmentGoods},false);
        Assert(player.EquipmentItems.Count==beforeReplayEquipment+1 && player.CashPoint==0,"Insufficient shop balance cannot grant a free item");player.CashPoint=cashSnapshot;

        // Restored ordinary shops are independent from the unrestricted boss shop.
        player.VipExp=0;
        foreach(uint level in new uint[]{1,21,31,41,51,61,71,81,91,99}) {
            player.Level=level;
            foreach(uint shopId in new uint[]{1,2}) {
                var ordinary=(await Page(shopId)).shopSetDataList.Single();
                var tier=catalog.Shops.Single(x=>x.ShopId==shopId).GoodsIdList.First(x=>!x.IsFirst&&x.PcLvMin<=level&&x.PcLvMax>=level);
                Assert(ordinary.goodsList.Count==tier.GoodsId.Count&&ordinary.goodsList.All(x=>x.packageId<FullShopCatalog.GoodsBase&&catalog.Goods.Any(g=>g.GoodsId==x.packageId)),"Original priced offers and tier slot count at level "+level+" shop "+shopId);
            }
        }
        player.Gold=1000000000;player.CashPoint=1000000000;
        var normal=(await Page(2)).shopSetDataList.Single();var first=normal.goodsList[0];var offer=catalog.Goods.Single(x=>x.GoodsId==first.packageId);
        uint balance=player.Gold;var normalBuy=await Buy(2,first.packageId);
        Assert(player.Gold==balance-offer.Gold&&normalBuy.shopSetData.goodsList[0].curStock==1,"Ordinary shop deducts original pack price and sells out the exact slot");
        balance=player.Gold;await Buy(2,first.packageId,retry:true);
        Assert(player.Gold==balance,"Ordinary sold-out purchase retry does not charge twice");
        await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new GsBuyIngameShopGoodsRequest {keyInfo=key,shopId=2,packageId=first.packageId},false);
        checks.Add("A new purchase cannot bypass sold-out stock");
        int locked=player.Markets[2].Slots.FindIndex(x=>x.Vip>0);
        await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new GsBuyIngameShopGoodsRequest {keyInfo=key,shopId=2,packageId=player.Markets[2].Slots[locked].GoodsId,SlotIndex=(uint)locked},false);
        checks.Add("Original VIP-gated slots reject VIP0");
        uint cash=player.CashPoint;var refreshed=Read<GsResetIngameShopSellingListOk>(await Request(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST,new GsResetIngameShopSellingListRequest {keyInfo=key,shopId=2}));
        Assert(player.CashPoint==cash-30&&refreshed.shopSetData.regenCount==1&&refreshed.shopSetData.goodsList.All(x=>x.curStock==0),"Manual refresh uses the original first 30-diamond expense and resets stock");
        cash=player.CashPoint;await Request(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST,new GsResetIngameShopSellingListRequest {keyInfo=key,shopId=2},retry:true);
        Assert(player.CashPoint==cash&&player.Markets[2].RefreshCount==1,"Manual refresh retry preserves currency and count");
        player.Markets[2].NextRefresh=ProtocolRouter.Now-1;var automatic=(await Page(2)).shopSetDataList.Single();
        Assert(automatic.nextRegenTime>ProtocolRouter.Now&&automatic.regenCount==1,"Expired six-hour stock refreshes without consuming a manual attempt");
        player.CashPoint=0;await Request(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST,new GsResetIngameShopSellingListRequest {keyInfo=key,shopId=2},false);checks.Add("Refresh rejects insufficient diamonds");player.CashPoint=cash;
        player.Markets[2].RefreshCount=(uint)catalog.VipFor(0).ShopGoodsMaxResetCount;
        await Request(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST,new GsResetIngameShopSellingListRequest {keyInfo=key,shopId=2},false);checks.Add("Refresh obeys the original daily VIP quota");player.Markets[2].RefreshCount=0;
        var leaked=new GsGetIngameShopSellingListRequest {keyInfo=key};leaked.shopId.AddRange(new[]{2u,FullShopCatalog.QueryMarker,equipmentGoods});
        await Request(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,leaked,false);checks.Add("Full catalogue queries cannot replace ordinary shop stock");
        byte[] ordinarySnapshot=JsonSerializer.SerializeToUtf8Bytes(player.Markets);
        uint potion=FullShopCatalog.GoodsBase+208002003;await Page(91,new[]{potion});int countBefore=player.Inventory.GetValueOrDefault(208002003u);balance=player.Gold;
        var bulk=new LocalShopBuyRequest {keyInfo=key,shopId=91,packageId=potion,buyCount=10};
        var bulkResult=Read<GsBuyIngameShopGoodsOk>(await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,bulk));
        Assert(player.Inventory.GetValueOrDefault(208002003u)==countBefore+10&&player.Gold==balance-10*full.Goods[potion].Gold,"Ten-pack purchase is one atomic exact-price transaction");
        uint afterBulk=player.Gold;await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,bulk,retry:true);
        Assert(player.Gold==afterBulk&&player.Inventory[208002003]==countBefore+10,"Bulk purchase replay is idempotent");
        bulk.buyCount=11;await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,bulk,false,true);checks.Add("Changed quantity on the same sequence is rejected");
        foreach(uint invalid in new uint[]{0,1000}) {bulk.buyCount=invalid;await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,bulk,false);}checks.Add("Zero and oversized batches cannot charge");
        int savedStack=player.Inventory[208002003];player.Inventory[208002003]=int.MaxValue-5;bulk.buyCount=10;
        await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,bulk,false);
        Assert(player.Gold==afterBulk&&player.Inventory[208002003]==int.MaxValue-5,"Inventory overflow rejects the entire batch without partial rewards");player.Inventory[208002003]=savedStack;
        await Page(91,new[]{equipmentGoods});int gearBefore=player.EquipmentItems.Count;
        var gears=new LocalShopBuyRequest{keyInfo=key,shopId=91,packageId=equipmentGoods,buyCount=3};
        var gearResult=Read<GsBuyIngameShopGoodsOk>(await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,gears));
        Assert(player.EquipmentItems.Count==gearBefore+3&&gearResult.deltaItemList.Select(x=>x.itemSeq).Distinct().Count()==3,"Bulk equipment creates independent instances");
        gears.buyCount=50;var maxGear=Read<GsBuyIngameShopGoodsOk>(await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,gears));
        Assert(maxGear.deltaItemList.Count==50&&WireCodec.Serialize(maxGear).Length<7000,"Maximum equipment batch stays inside the original receive buffer");
        gears.buyCount=51;await Request(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,gears,false);checks.Add("Equipment batch cap protects the original receive buffer");
        Assert(ordinarySnapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(player.Markets)),"Boss-shop browsing and bulk buys never change ordinary stock");
        server.Store.Save();var marketReload=new PlayerStore(save,catalog).Find(player.Uid)!;
        Assert(JsonSerializer.Serialize(player.Markets)==JsonSerializer.Serialize(marketReload.Markets),"Original slots, stock, schedule and refresh counts survive server restart");

        // Native VIP packages use original requirements, original sale prices, and original contents.
        var firstVip=catalog.Vip.First(x=>x.VipLv==1);player.VipExp=0;
        await Request(GameServerCommand.GS_BUY_VIP_REWARD_REQUEST,new GsBuyVipRewardRequest {keyInfo=key,packageId=(uint)firstVip.LvUpRewardId},false);checks.Add("VIP package purchase rejects insufficient VIP level");
        player.VipExp=(uint)catalog.Vip.Max(x=>x.LvUpNeedVipExp);player.CashPoint=1_000_000_000;
        foreach(var vip in catalog.Vip.Where(x=>x.LvUpRewardId>0)) {
            var package=catalog.Packages.Single(x=>x.Id==vip.LvUpRewardId);uint before=player.CashPoint;
            var result=Read<GsBuyVipRewardOk>(await Request(GameServerCommand.GS_BUY_VIP_REWARD_REQUEST,new GsBuyVipRewardRequest {keyInfo=key,packageId=(uint)package.Id}));
            uint cashReward=(uint)package.ItemIdType.Select((x,i)=>x==BscTable.REWARD_TYPE.REWARD_TYPE_CASH?package.Quantity[i]:0).Sum();
            Assert(player.CashPoint==before-(uint)package.Price+cashReward && player.PurchasedVipPackages.Contains((uint)package.Id) && result.deltaItemList.Count+result.deltaCardList.Count>0,"Original VIP "+vip.VipLv+" package price and rewards settle");
        }
        var history=Read<GsGetVipRewardPurchaseHistoryOk>(await Request(GameServerCommand.GS_GET_VIP_REWARD_PURCHASE_HISTORY_REQUEST,new GsGetVipRewardPurchaseHistoryRequest {keyInfo=key}));
        Assert(history.packageId.Count==20 && history.packageId.Distinct().Count()==20,"Native VIP history contains exactly the twenty purchased tier packages");
        uint vipCash=player.CashPoint;
        await Request(GameServerCommand.GS_BUY_VIP_REWARD_REQUEST,new GsBuyVipRewardRequest {keyInfo=key,packageId=1},false);
        Assert(player.CashPoint==vipCash && player.PurchasedVipPackages.Count==20,"A new sequence cannot repurchase a once-only VIP package");

        // The original system shop offers retain VIP daily stock and tiered price ratios.
        var system=Read<GsGetSystemShopInfoOk>(await Request(GameServerCommand.GS_GET_SYSTEM_SHOP_INFO_REQUEST,new GsGetSystemShopInfoRequest {keyInfo=key}));
        Assert(system.SlotData.Select(x=>x.slotId).SequenceEqual(catalog.SystemShops.Select(x=>(uint)x.SlotId)),"System shop slot history follows original table order");
        foreach(var slot in catalog.SystemShops.Where(x=>x.OpenVVIPLevel==0)) {
            var good=catalog.Goods.Single(x=>x.GoodsId==slot.GoodsId);int max=slot.BuyTimeVIPDataList.OrderByDescending(x=>x.VipLevel).First().BuyCount;
            uint before=player.CashPoint;
            var result=Read<GsBuySystemShopGoodsOk>(await Request(GameServerCommand.GS_BUY_SYSTEM_SHOP_GOODS_REQUEST,new GsBuySystemShopGoodsRequest {keyInfo=key,SlotId=(uint)slot.SlotId,buyTryCount=1}));
            var ratio=catalog.SystemShopPrices.Single(x=>x.ExpenseType==slot.RatioType).SystemShopExpenseWeightTryTimeDataList.First(x=>x.TryTime==1).Ratio;
            Assert(player.CashPoint==before-(uint)Math.Ceiling(good.Cash*(100+ratio)/100d) && player.SystemShopPurchases[(uint)slot.SlotId]==1 && result.deltaItemList.Count+result.deltaCardList.Count>0,"System slot "+slot.SlotId+" uses original item and first purchase price");
            await Request(GameServerCommand.GS_BUY_SYSTEM_SHOP_GOODS_REQUEST,new GsBuySystemShopGoodsRequest {keyInfo=key,SlotId=(uint)slot.SlotId,buyTryCount=(uint)max},false);
        }
        var vvip=catalog.SystemShops.First(x=>x.OpenVVIPLevel>0);
        await Request(GameServerCommand.GS_BUY_SYSTEM_SHOP_GOODS_REQUEST,new GsBuySystemShopGoodsRequest {keyInfo=key,SlotId=(uint)vvip.SlotId,buyTryCount=1},false);checks.Add("Unavailable platform VVIP cannot bypass its original requirement");

        // Gold conversion uses original first/next prices and returns every result for native animation.
        player.VipExp=0;player.GoldExchangeCount=0;player.GoldExchangeMisses=0;player.GoldCriticalForbid=0;player.GoldCriticalStreak=0;player.Gold=0;player.CashPoint=100000;
        var exchange=await ExchangeInfo();Assert(exchange.exchangeToGoldCount==0 && exchange.exchangeGoldInfo!=null,"Gold exchange info is fully initialized");
        uint beforeCash=player.CashPoint;
        var one=Read<GsExchangeGoldOK>(await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key}));
        Assert(one.ExchangeGoldData.Count==1 && player.CashPoint==beforeCash-catalog.CriticalGold[0].Cost && player.Gold==one.ExchangeGoldData[0].ExchangeGold,"Single gold exchange deducts original diamonds and grants the declared gold");
        uint exchangeSequence=key.sequenceNo;
        uint exchangeGold=player.Gold,exchangeCash=player.CashPoint;
        var exchangeReplay=Read<GsExchangeGoldOK>(await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key},retry:true));
        Assert(player.Gold==exchangeGold && player.CashPoint==exchangeCash && player.GoldExchangeCount==1 && WireCodec.Serialize(exchangeReplay).SequenceEqual(WireCodec.Serialize(one)), "Gold exchange retransmission preserves money, counters and the original random result");
        await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key,continuousExchange=true},false,true);
        Assert(player.Gold==exchangeGold && player.CashPoint==exchangeCash, "A reused sequence with a different gold-exchange option is rejected");
        uint beforeGold=player.Gold;
        var chain=Read<GsExchangeGoldOK>(await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key,continuousExchange=true}));
        Assert(chain.ExchangeGoldData.Count==2 && player.GoldExchangeCount==3 && player.Gold==beforeGold+chain.ExchangeGoldData.Sum(x=>(long)x.ExchangeGold),"VIP0 chain exchange stops at its remaining original daily quota");
        uint capCash=player.CashPoint;
        await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key},false);
        Assert(player.CashPoint==capCash,"Exhausted gold exchange quota cannot deduct diamonds");
        string GoldState()=>JsonSerializer.Serialize(new {player.Gold,player.CashPoint,player.GoldExchangeCount,player.GoldExchangeMisses,player.GoldCriticalCount,player.GoldCriticalStreak,player.GoldCriticalForbid,player.Inventory});
        string state=GoldState();
        var simulation=Read<GsExchangeGoldSimulateOk>(await Request(GameServerCommand.GS_EXCHANGE_GOLD_SIMULATE_REQUEST,new GsExchangeGoldSimulateRequest {keyInfo=key,exchangeCount=10}));
        Assert(simulation.exchangeData.Count==10 && state==GoldState(),"Gold simulation never changes money, counters, pity state or inventory");
        player.ExchangeDay--;
        var reset=await ExchangeInfo();Assert(reset.exchangeToGoldCount==0 && player.SystemShopPurchases.Count==0 && player.PurchasedVipPackages.Count==20,"Daily reset clears exchange/system quotas while preserving once-only VIP rewards");
        player.CashPoint=0;uint insufficientGold=player.Gold;state=JsonSerializer.Serialize(player);
        await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key},false);
        Assert(player.GoldExchangeCount==0 && player.Gold==insufficientGold && player.CashPoint==0,"Insufficient gold-exchange balance cannot consume quota");
        player.CashPoint=1000;player.Gold=uint.MaxValue;
        await Request(GameServerCommand.GS_EXCHANGE_GOLD_REQUEST,new GsExchangeGoldRequest {keyInfo=key},false);
        Assert(player.CashPoint==1000 && player.Gold==uint.MaxValue && player.GoldExchangeCount==0,"Overflowing gold exchange is rejected atomically");

        // Sales are planned atomically, and equipment is removed by its serial.
        player.Gold=1000;
        var goldItem=catalog.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GOLDITEM && x.Sell>0 && x.SellMoneyType==BscTable.ITEM_SELL_MONEY_TYPE.SELL_MONEY_TYPE_GOLD);
        uint goldGoods=FullShopCatalog.GoodsBase+(uint)goldItem.Id;
        player.Gold=10000000;await Page(91,new[]{goldGoods});await Buy(91,goldGoods);
        int ownedGoldItems=player.Inventory.GetValueOrDefault((uint)goldItem.Id);uint saleBeforeGold=player.Gold;
        var invalidSale=new GsSellUserItemRequest {keyInfo=key};invalidSale.sellItemInfo.Add(new PKUserItem {itemID=(uint)goldItem.Id,itemSeq=(uint)goldItem.Id,itemCount=1});invalidSale.sellItemInfo.Add(new PKUserItem {itemID=uint.MaxValue,itemSeq=uint.MaxValue,itemCount=1});
        await Request(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,invalidSale,false);
        Assert(player.Gold==saleBeforeGold && player.Inventory[(uint)goldItem.Id]==ownedGoldItems, "Invalid mixed sales cannot partly remove items or grant money");
        var sell=new GsSellUserItemRequest {keyInfo=key};sell.sellItemInfo.Add(new PKUserItem {itemID=(uint)goldItem.Id,itemSeq=(uint)goldItem.Id,itemCount=1});
        var sold=Read<GsSellUserItemOk>(await Request(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,sell));
        Assert(player.Gold==saleBeforeGold+goldItem.Value && player.Inventory[(uint)goldItem.Id]==ownedGoldItems-1 && sold.deltaItemList.Single().itemCount==-1, "Original gold-item auto-sale can settle after a market purchase");
        uint afterSaleGold=player.Gold;await Request(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,sell,retry:true);
        Assert(player.Gold==afterSaleGold && player.Inventory[(uint)goldItem.Id]==ownedGoldItems-1, "Sale retransmission cannot remove or sell the item twice");
        var sellableEquipment=catalog.Items.FirstOrDefault(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT && x.Sell>0);
        if(sellableEquipment!=null) {
            uint saleGoods=FullShopCatalog.GoodsBase+(uint)sellableEquipment.Id;player.Gold=1_000_000_000;await Page(91,new[]{saleGoods});
            var acquired=await Buy(91,saleGoods);ulong selectedSequence=acquired.deltaItemList.Single().itemSeq;
            int equipmentCount=player.EquipmentItems.Count;var equipmentSale=new GsSellUserItemRequest {keyInfo=key};equipmentSale.sellItemInfo.Add(new PKUserItem {itemID=(uint)sellableEquipment.Id,itemSeq=selectedSequence,itemCount=1});
            await Request(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,equipmentSale);
            Assert(player.EquipmentItems.Count==equipmentCount-1 && player.EquipmentItems.All(x=>x.Sequence!=selectedSequence), "Selling equipment removes only the selected owned instance");
        } else {
            var unsellable=player.EquipmentItems.First();int equipmentCount=player.EquipmentItems.Count;var rejectedSale=new GsSellUserItemRequest {keyInfo=key};rejectedSale.sellItemInfo.Add(new PKUserItem {itemID=unsellable.ItemId,itemSeq=unsellable.Sequence,itemCount=1});
            await Request(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,rejectedSale,false);
            Assert(player.EquipmentItems.Count==equipmentCount, "Equipment without an original sell price cannot be silently deleted by a sale");
        }
        var equipmentList=Read<GsGetUserItemListOk>(await Request(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest {keyInfo=key,reqType=InventoryType.INVENTORY_TYPE_EQUIPMENT_ITEM}));
        while(equipmentList.UserItemList.Count<equipmentList.totalDataCount) {
            using var inventoryTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var part=codec.Decode((await WireCodec.ReadFrame(game.GetStream(),inventoryTimeout.Token))!,true);
            if(part.Command!=(ushort)GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_OK)throw new Exception("Unexpected multipart inventory command");
            equipmentList.UserItemList.AddRange(Read<GsGetUserItemListOk>(part.Payload).UserItemList);
        }
        Assert(equipmentList.UserItemList.Count==player.EquipmentItems.Count && equipmentList.UserItemList.All(x=>x.itemSeq>uint.MaxValue), "Equipment inventory filter returns stable instance IDs without stackable items");

        // Reload and inventory multipart correctness for independently owned equipment.
        server.Store.Save();var restored=new PlayerStore(save,catalog).Find(player.Uid)!;
        Assert(restored.EquipmentItems.Select(x=>x.Sequence).SequenceEqual(player.EquipmentItems.Select(x=>x.Sequence)) && restored.PurchasedVipPackages.SetEquals(player.PurchasedVipPackages) && restored.ShopReceipts.Count>0,"Equipment identities, VIP ownership and replay receipts survive reload");
        byte[]? after=File.Exists(production)?SHA256.HashData(File.ReadAllBytes(production)):null;
        Assert(beforeProduction==null?after==null:beforeProduction.SequenceEqual(after!),"Shop tests never modify the real profile");
        File.WriteAllText(Path.Combine(root,"evidence/shop-protocol-test.json"),JsonSerializer.Serialize(new {passed=checks.Count,checks,saveDirectory=save,catalogCount=full.Goods.Count,productionSaveUsed=false},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("PASS: "+checks.Count+" shop, VIP and gold exchange checks.");
    }
}
