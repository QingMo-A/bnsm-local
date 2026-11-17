using System.Text.Json;

internal static class EssenceShopTest
{
    private sealed class Peer : IDisposable
    {
        private readonly System.Net.Sockets.TcpClient socket = new();
        private readonly WireCodec codec;
        public SessionKeyInfo Key = new();
        public byte[] Last = [];
        public Peer(string root) => codec = new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        private async Task<(ushort, byte[])> Send(System.Net.Sockets.TcpClient client, ushort command, object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(request), true));
            using var timeout = new CancellationTokenSource(10000);
            var reply = codec.Decode((await WireCodec.ReadFrame(client.GetStream(), timeout.Token))!, true);
            return (reply.Command, reply.Payload);
        }
        public async Task Login(LocalRuntime server)
        {
            using var login = new System.Net.Sockets.TcpClient();
            await login.ConnectAsync("127.0.0.1", server.LoginPort);
            await Send(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = "local-player", userPW = "local", osType = DeviceOSType.DEVICE_PC });
            var start = Read<LsGameStartOk>((await Send(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true })).Item2);
            Key = new() { uid = start.uid, loginSessionKey = start.loginSessionKey };
            await socket.ConnectAsync("127.0.0.1", server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command, object request, bool success = true, bool retry = false)
        {
            if (!retry) Key.sequenceNo++;
            var response = await Send(socket, (ushort)command, request);
            string expected = command.ToString().Replace("_REQUEST", success ? "_OK" : "_FAIL");
            if (((GameServerCommand)response.Item1).ToString() != expected) throw new Exception(expected + ": " + Read<FailPacket>(response.Item2).message);
            Last = response.Item2;
            return Read<T>(response.Item2);
        }
        public void Dispose() => socket.Dispose();
    }
    private static T Read<T>(byte[] bytes) => (T)WireCodec.Deserialize(typeof(T), bytes)!;
    private static LocalPlayer Prepare(LocalRuntime runtime)
    {
        var player = runtime.Store.GetOrCreate("local-player");
        player.Level = 99; player.Exp = ProgressionRules.PlayerFloor(runtime.Catalog, 99);
        player.Gold = 500000; player.CashPoint = 10000; player.WorldBossPoint = 1000000; player.VipExp = 2000000;
        ulong day = AttendanceRules.DayIndex(ProtocolRouter.Now);
        player.AttendanceDays = 1; player.AttendanceLastDay = day; player.AttendanceVipClaimedDay = day;
        player.ShortTermDays = 1; player.ShortTermLastDay = day;
        runtime.Store.Save();
        return player;
    }
    public static async Task Fixture(string root, string output)
    {
        output = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(root, "verification")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Isolated essence fixture required");
        Directory.CreateDirectory(output);
        await using var runtime = new LocalRuntime(root, Path.Combine(output, "saves"), 0, 0, 0, false);
        if (runtime.Store.All().Count == 0) Prepare(runtime);
        File.WriteAllText(Path.Combine(output, "endpoints.json"), JsonSerializer.Serialize(new { directoryPort = runtime.DirectoryPort, productionSaveUsed = false }));
        await Task.Delay(TimeSpan.FromHours(2));
    }
    public static async Task Run(string root)
    {
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        string save = Path.Combine(root, "verification/essence-shop", Guid.NewGuid().ToString("N"));
        uint remaining = 0;
        await using (var runtime = new LocalRuntime(root, save, 0, 0, 0, false))
        {
            var player = Prepare(runtime); var catalog = runtime.Catalog;
            var shop = catalog.Shops.Single(x => x.ShopId == 3);
            uint opening = (uint)catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SHOP_ESSENCE);
            File.WriteAllText(Path.Combine(root, "evidence/essence-shop-catalog.json"), JsonSerializer.Serialize(new {
                opening, shop.ShopId, shop.ResetActivate, shop.ResetTime, shop.ResetTerm, shop.ResetCostType, shop.ResetCostValue,
                tiers = shop.GoodsIdList.Select(x => new { x.PcLvMin, x.PcLvMax, slots = x.GoodsId.Count, x.VipLevel, x.DiscountRatio }),
                originalEssenceOffers = catalog.Goods.Count(x => x.BossP > 0 && x.Quantity > 0 && x.ProductIdType == BscTable.GOODS_TYPE.GOODS_TYPE_ITEM && catalog.ItemsById.ContainsKey(x.ProductId)),
                selection = "The original CDT omits GoodsGroup membership/weights. Use distinct eligible original BossP-priced offers equally; preserve prices, quantities, tier slots and gates."
            }, new JsonSerializerOptions { WriteIndented = true }));
            using var peer = new Peer(root); await peer.Login(runtime);
            async Task<PKShopSetData> Page()
            {
                var request = new GsGetIngameShopSellingListRequest { keyInfo = peer.Key }; request.shopId.Add(3);
                return (await peer.Request<GsGetIngameShopSellingListOk>(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST, request)).shopSetDataList.Single();
            }
            GsBuyIngameShopGoodsRequest Buy(PKShopSetData page, int index) => new() { keyInfo = peer.Key, shopId = 3, SlotIndex = (uint)index, packageId = page.goodsList[index].packageId };
            string State() => JsonSerializer.Serialize(new { player.Gold, player.CashPoint, player.WorldBossPoint, player.GuildTokens, player.Inventory, player.EquipmentItems, player.Markets });
            async Task Bad(GsBuyIngameShopGoodsRequest request, ServerErrorCode error, string label)
            {
                string state = State();
                var reply = await peer.Request<FailPacket>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST, request, false);
                Check(reply.errorCode == (uint)error && State() == state, label);
            }
            foreach (uint level in new uint[] { 1, 21, 31, 41, 51, 61, 71, 81, 91, 99 }.Where(x => x >= opening))
            {
                player.Level = level;
                var page = await Page();
                var tier = shop.GoodsIdList.Where(x => !x.IsFirst && x.PcLvMin <= level).OrderByDescending(x => x.PcLvMin).First();
                Check(page.goodsList.Count == tier.GoodsId.Count && page.goodsList.All(x => x.moneytype == BscTable.MONEY_TYPE.MONEY_TYPE_WORLD_BOSS_POINT && catalog.Goods.Any(g => g.GoodsId == x.packageId && g.BossP > 0)), "Original essence offers and currency at level " + level);
            }
            player.Level = 99; var offers = await Page();
            Check(offers.goodsList.Select(x => x.packageId).Distinct().Count() == offers.goodsList.Count, "No duplicate offers within a refresh");
            string stock = JsonSerializer.Serialize(offers);
            Check(stock == JsonSerializer.Serialize(await Page()), "Reopening preserves current stock and refresh deadline");
            if (opening > 1)
            {
                player.Level = opening - 1;
                await Bad(Buy(offers, 0), ServerErrorCode.ERROR_INVALID_USER_LEVEL, "Locked levels cannot bypass the opening requirement");
                player.Level = 99;
            }
            player.WorldBossPoint = 0;
            await Bad(Buy(offers, 0), ServerErrorCode.ERROR_NOT_ENOUGH_WORLD_BOSS_POINT, "Gold and diamonds cannot substitute for insufficient essence");
            player.WorldBossPoint = 1000000;
            var wrong = Buy(offers, 0); wrong.SlotIndex = (uint)offers.goodsList.Count;
            await Bad(wrong, ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET, "Forged slot cannot consume currency or grant goods");
            var good = catalog.Goods.Single(x => x.GoodsId == offers.goodsList[0].packageId);
            var slot = player.Markets[3].Slots[0];
            uint price = (uint)Math.Ceiling(good.BossP * (100 - slot.Discount) / 100d);
            uint gold = player.Gold, cash = player.CashPoint, essence = player.WorldBossPoint, tokens = player.GuildTokens;
            var purchase = Buy(offers, 0);
            var result = await peer.Request<GsBuyIngameShopGoodsOk>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST, purchase);
            Check(player.WorldBossPoint == essence - price && result.nowCoinInfo.worldBossPoint == player.WorldBossPoint, "Purchase deducts exactly the displayed original essence price");
            Check(player.Gold == gold && player.CashPoint == cash && player.GuildTokens == tokens, "Essence purchase leaves the other wallets unchanged");
            Check(result.deltaItemList.Sum(x => x.itemCount) == good.Quantity && result.deltaItemList.All(x => x.itemID == good.ProductId), "Purchase grants exactly the original pack quantity");
            Check(result.shopSetData.goodsList[0].curStock == 1, "Purchased slot becomes sold out");
            string after = State(); byte[] receipt = peer.Last.ToArray();
            await peer.Request<GsBuyIngameShopGoodsOk>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST, purchase, retry: true);
            Check(after == State() && receipt.SequenceEqual(peer.Last), "Purchase retry replays its receipt without another charge");
            await Bad(Buy(offers, 0), ServerErrorCode.ERROR_OVER_MAX_COUNT, "A new request cannot buy a sold-out slot again");
            uint refreshCount = player.Markets[3].RefreshCount;
            if (shop.ResetActivate)
            {
                uint cost = offers.nextRegenCost; cash = player.CashPoint; essence = player.WorldBossPoint;
                var reset = new GsResetIngameShopSellingListRequest { keyInfo = peer.Key, shopId = 3 };
                var refresh = await peer.Request<GsResetIngameShopSellingListOk>(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST, reset);
                Check(player.CashPoint == cash - cost && player.WorldBossPoint == essence && refresh.shopSetData.regenCount == refreshCount + 1 && refresh.shopSetData.goodsList.All(x => x.curStock == 0), "Manual refresh charges original diamonds, preserves essence and resets stock");
                after = State(); await peer.Request<GsResetIngameShopSellingListOk>(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST, reset, retry: true);
                Check(State() == after, "Refresh retry does not charge twice");
                player.CashPoint = 0; after = State();
                var failure = await peer.Request<FailPacket>(GameServerCommand.GS_RESET_INGAME_SHOP_SELLING_LIST_REQUEST, reset, false);
                Check(failure.errorCode == (uint)ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT && State() == after, "Insufficient refresh diamonds leave stock and wallets unchanged");
                player.CashPoint = cash;
            }
            refreshCount = player.Markets[3].RefreshCount; essence = player.WorldBossPoint; cash = player.CashPoint;
            player.Markets[3].NextRefresh = ProtocolRouter.Now - 1;
            var automatic = await Page();
            Check(automatic.nextRegenTime > ProtocolRouter.Now && automatic.regenCount == refreshCount && automatic.goodsList.All(x => x.curStock == 0) && player.WorldBossPoint == essence && player.CashPoint == cash, "Timed refresh restores stock without spending currency or manual attempts");
            var overflow = automatic.goodsList.Select((x, i) => (goods: catalog.Goods.Single(g => g.GoodsId == x.packageId), i)).FirstOrDefault(x => catalog.ItemsById[x.goods.ProductId].ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT);
            if (overflow.goods != null)
            {
                uint id = (uint)overflow.goods.ProductId; int old = player.Inventory.GetValueOrDefault(id); player.Inventory[id] = int.MaxValue;
                after = State(); await peer.Request<FailPacket>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST, Buy(automatic, overflow.i), false);
                Check(State() == after, "Inventory overflow cannot partially charge essence"); player.Inventory[id] = old;
            }
            runtime.Store.Save(); remaining = player.WorldBossPoint;
        }
        await using (var runtime = new LocalRuntime(root, save, 0, 0, 0, false))
        {
            var player = runtime.Store.All().Single();
            Check(player.WorldBossPoint == remaining && player.Markets.ContainsKey(3) && player.ShopReceipts.Count > 0, "Essence wallet, shop stock and purchase receipts survive restart");
        }
        File.WriteAllText(Path.Combine(root, "evidence/essence-shop-protocol-test.json"), JsonSerializer.Serialize(new { passed = true, checks, save, productionSaveUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("ESSENCE_SHOP_TEST_OK " + checks.Count);
    }
}
