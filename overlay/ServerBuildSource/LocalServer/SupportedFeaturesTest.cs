using System.Net.Sockets;
using System.Text.Json;

internal static class SupportedFeaturesTest
{
    public static async Task Run(string root)
    {
        string save = Path.Combine(root, "verification/features", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Assert(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        await using var server = new LocalRuntime(root, save, 0, 0, 0);
        async Task<(ushort Command, byte[] Payload)> Exchange(TcpClient socket, ushort command, object message)
        {
            await socket.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(message), true));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = codec.Decode((await WireCodec.ReadFrame(socket.GetStream(), timeout.Token))!, true);
            return (response.Command, response.Payload);
        }
        T Read<T>(byte[] payload) => (T)WireCodec.Deserialize(typeof(T), payload)!;
        using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
        await Exchange(login, (ushort)LoginServerCommand.LS_VERSION_CHECK_REQUEST, new LsVersionCheckRequest { checkVersion = 1 });
        var logged = await Exchange(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST,
            new LsLoginRequest { userID = "features-test", userPW = "local-only", osType = DeviceOSType.DEVICE_PC });
        Assert(logged.Command == (ushort)LoginServerCommand.LS_LOGIN_OK, "Feature test uses a separate local account");
        var account = Read<LsGameStartOk>((await Exchange(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST,
            new LsGameStartRequest { serverId = 1, bAgreement = true })).Payload);
        var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
        using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
        async Task<(ushort Command, byte[] Payload)> Request(GameServerCommand command, object message) => await Exchange(game, (ushort)command, message);
        async Task<T> Ok<T>(GameServerCommand command, object message)
        {
            var response = await Request(command, message);
            if (((GameServerCommand)response.Command).ToString() != command.ToString().Replace("_REQUEST", "_OK"))
                throw new Exception("Expected OK: " + command);
            return Read<T>(response.Payload);
        }
        async Task<FailPacket> Fail(GameServerCommand command, object message)
        {
            var response = await Request(command, message);
            if (((GameServerCommand)response.Command).ToString() != command.ToString().Replace("_REQUEST", "_FAIL"))
                throw new Exception("Expected FAIL: " + command);
            return Read<FailPacket>(response.Payload);
        }
        async Task<GsGetCashBalanceOk> Balance() => await Ok<GsGetCashBalanceOk>(GameServerCommand.GS_GET_CASH_BALANCE_REQUEST, new GsGetCashBalanceRequest { keyInfo = key });
        var profile = await Ok<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key });
        Assert(profile.namecardInfo.newbieFlag == NEWBIE_FLAG_TYPE.NEWBIE_FLAG_OLDBIE && profile.tutorialFlags.All(x => x == 255), "Preconfigured local character bypasses obsolete onboarding");
        var choice = await Request(GameServerCommand.GS_SET_USER_NEWBIE_FLAG_REQUEST, new GsSetUserNewbieFlagRequest { keyInfo = key, newbieType = NEWBIE_FLAG_TYPE.NEWBIE_FLAG_NEWBIE });
        Assert(choice.Command == (ushort)GameServerCommand.GS_SET_USER_NEWBIE_FLAG_OK, "Tutorial mode selection is accepted");
        Assert(new PlayerStore(save, server.Catalog).Find(account.uid)!.NewbieFlag == NEWBIE_FLAG_TYPE.NEWBIE_FLAG_NEWBIE, "Tutorial selection survives save reload");
        await Fail(GameServerCommand.GS_SET_USER_NEWBIE_FLAG_REQUEST, new GsSetUserNewbieFlagRequest { keyInfo = key, newbieType = NEWBIE_FLAG_TYPE.NEWBIE_FLAG_NONE });
        Assert(server.Store.Find(account.uid)!.NewbieFlag == NEWBIE_FLAG_TYPE.NEWBIE_FLAG_NEWBIE, "Invalid tutorial mode cannot corrupt selection");
        var notices = await Ok<GsGetSystemNoticeOk>(GameServerCommand.GS_GET_SYSTEM_NOTICE_REQUEST, new GsGetSystemNoticeRequest { keyInfo = key });
        Assert(notices.noticeList.Count > 0 && notices.noticeList.All(x => !string.IsNullOrWhiteSpace(x)), "Local announcements contain readable content");
        var missions = await Ok<GsGetChangedMissionListOk>(GameServerCommand.GS_GET_CHANGED_MISSION_LIST_REQUEST,
            new GsGetChangedMissionListRequest { keyInfo = key });
        Assert(missions.missionInfo != null && missions.list.Any(x => x.missionId == 310 && x.status == MissionStatus.MISSION_STATUS_PROGRESSING), "New local account receives original daily gacha mission 310");
        var unchangedMissions = await Ok<GsGetChangedMissionListOk>(GameServerCommand.GS_GET_CHANGED_MISSION_LIST_REQUEST,
            new GsGetChangedMissionListRequest { keyInfo = key });
        Assert(unchangedMissions.missionInfo != null && unchangedMissions.list.Count == 0, "Unchanged mission refresh does not repeat notifications");
        var attendance = await Ok<GsGetShortTermAttendanceRewardInfoOk>(GameServerCommand.GS_GET_SHORTTERM_ATTENDANCE_REWARD_INFO_REQUEST,
            new GsGetShortTermAttendanceRewardInfoRequest { keyInfo = key });
        Assert(attendance.rewardInfoList.Count > 0 && attendance.rewardInfoList.Select((x, i) => (int)x.accrueCount == i + 1).All(x => x),
            "Short-term attendance returns the configured reward ladder in day order");
        async Task SaveSettings(ulong value) {
            var response = await Request(GameServerCommand.GS_SAVE_USER_SETTING_REQUEST, new GsSaveUserSettingRequest {keyInfo=key,settings=value});
            Assert(response.Command==(ushort)GameServerCommand.GS_SAVE_USER_SETTING_OK, "Settings save returns the original empty OK acknowledgement: "+value);
        }
        Assert(profile.settings==0, "Old saves without UserSettings retain the original all-enabled default");
        ulong mask=32 | (ulong)PushOnOffBitFlag.STAMINA_QUEST_TIME;
        await SaveSettings(mask);
        var settingsInfo=await Ok<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest {keyInfo=key});
        Assert(settingsInfo.settings==mask, "Settings are returned on the real encrypted game-info protocol");
        await SaveSettings(mask);
        Assert(new PlayerStore(save,server.Catalog).Find(account.uid)!.UserSettings==mask, "Repeated absolute settings saves survive a disk reload");
        await SaveSettings(ulong.MaxValue);
        Assert(new PlayerStore(save,server.Catalog).Find(account.uid)!.UserSettings==ulong.MaxValue, "Unknown future flag bits and unsigned 64-bit values survive JSON persistence");
        await SaveSettings(32);
        Assert(new PlayerStore(save,server.Catalog).Find(account.uid)!.UserSettings==32, "All-disabled notifications retain the initialized marker");
        var invalidSettings=await Fail(GameServerCommand.GS_SAVE_USER_SETTING_REQUEST,new GsSaveUserSettingRequest {keyInfo=new SessionKeyInfo {uid=account.uid,loginSessionKey="invalid"},settings=0});
        Assert(invalidSettings.errorCode==(uint)ServerErrorCode.ERROR_INVALID_SESSION_KEY && server.Store.Find(account.uid)!.UserSettings==32, "Unauthenticated settings requests cannot change the save");
        var pools = await Ok<GsGetUserGachaInfoOk>(GameServerCommand.GS_GET_USER_GACHA_INFO_REQUEST, new GsGetUserGachaInfoRequest { keyInfo = key });
        Assert(pools.GachaStackCount.Count == 10 && pools.GachaStackCount.Single(x => x.GachaGroupIndex == 3).RemainFreeCount == 1, "All ten card/material/VIP categories report original free-draw limits");
        var rejected = await Fail(GameServerCommand.GS_GACHA_REQUEST, new GsGachaRequest { keyInfo = key, gachaGroupIndex = 4 });
        Assert(rejected.errorCode == (uint)ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT && server.Store.Find(account.uid)!.CardIds.Count == 5, "Insufficient funds cannot create cards");
        key.sequenceNo++;
        var freeRequest = new GsGachaRequest { keyInfo = key, gachaGroupIndex = 3, useFreeCount = true };
        var free = await Ok<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST, freeRequest);
        int afterFreeCards = server.Store.Find(account.uid)!.CardIds.Count;
        Assert(free.newCardList.Count + free.newItemList.Count == 1 && free.gachaStackCount.RemainFreeCount == 0 && free.currentCoinInfo.cashPoint == 0, "Free draw grants one visible card or item without charging currency");
        var repeat = await Ok<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST, freeRequest);
        Assert(WireCodec.Serialize(repeat).SequenceEqual(WireCodec.Serialize(free)) && server.Store.Find(account.uid)!.CardIds.Count == afterFreeCards, "Retrying the same draw sequence cannot duplicate rewards");
        key.sequenceNo++;
        await Fail(GameServerCommand.GS_GACHA_REQUEST, freeRequest);
        Assert(server.Store.Find(account.uid)!.CardIds.Count == afterFreeCards, "Exhausted daily free draws are rejected");
        await Fail(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST, new GsBuyCashProductRequest { keyInfo = key, cashProductId = uint.MaxValue });
        Assert((await Balance()).coinInfo.cashPoint == 0, "Invalid recharge product cannot grant currency");
        var order = await Ok<GsBuyCashProductOk>(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST, new GsBuyCashProductRequest { keyInfo = key, cashProductId = 8 });
        var sameOrder = await Ok<GsBuyCashProductOk>(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST, new GsBuyCashProductRequest { keyInfo = key, cashProductId = 8 });
        Assert(order.tokenId.StartsWith("LOCAL-") && order.tokenId == sameOrder.tokenId && (await Balance()).coinInfo.cashPoint == 0, "Local order creation is idempotent and awaits delivery");
        Assert(new PlayerStore(save, server.Catalog).Find(account.uid)!.PendingCharge!.Token == order.tokenId, "Pending simulated order is persisted before delivery");
        var delivered = await Ok<GsCashProductDeliveryOk>(GameServerCommand.GS_CASH_PRODUCT_DELIVERY_REQUEST, new GsCashProductDeliveryRequest { keyInfo = key });
        Assert(delivered.deliveryCount == 1 && delivered.coinInfo.cashPoint == 6480, "Simulated delivery grants the original 6480-unit product");
        key.sequenceNo++;
        var deliveredAgain = await Ok<GsCashProductDeliveryOk>(GameServerCommand.GS_CASH_PRODUCT_DELIVERY_REQUEST, new GsCashProductDeliveryRequest { keyInfo = key });
        Assert(deliveredAgain.deliveryCount == 0 && deliveredAgain.coinInfo.cashPoint == 6480, "Repeated delivery cannot grant currency twice");
        key.sequenceNo++;
        int drawRank = GachaRules.Load(root).CardRank;
        int cardsBeforeTen = server.Store.Find(account.uid)!.CardIds.Count(x => x != 0);
        var ten = await Ok<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST, new GsGachaRequest { keyInfo = key, gachaGroupIndex = 4 });
        Assert(ten.newCardList.Count + ten.newItemList.Count == 10 && ten.existCardIdList.Count == 0 && ten.currentCoinInfo.cashPoint == 4200, "Ten draws deduct original 2280-unit cost and grant exactly ten visible results");
        var pool = server.Catalog.DrawPool(true, drawRank);
        Assert(ten.newCardList.All(card => pool.Any(c => c.Id == card.cardID)) &&
            (ten.newCardList.Any(card => pool.Any(c => c.Id == card.cardID && c.Talent >= BscTable.CARD_TALENT.CARD_TALENT_S)) ||
             ten.newItemList.Any(item => pool.Any(c => c.PieceId == item.itemID && c.Talent >= BscTable.CARD_TALENT.CARD_TALENT_S))), "Configured card pool and S-or-better ten-draw guarantee allow matching duplicate fragments");
        Assert(ten.newItemList.All(item => server.Catalog.Items.Any(x => x.Id == item.itemID) && item.itemCount > 0), "Fragment and resource rewards are valid visible inventory items");
        int cardsAfterTen = cardsBeforeTen + ten.newCardList.Count;
        var saved = new PlayerStore(save, server.Catalog).Find(account.uid)!;
        Assert(saved.CardIds.Count(x => x != 0) == cardsAfterTen && saved.CashPoint == ten.currentCoinInfo.cashPoint && saved.Gacha[3].FreeUsed == 1 && saved.PendingCharge == null, "Cards, balance including resource rewards, free-draw use and settled order survive reload");
        key.sequenceNo++;
        await Fail(GameServerCommand.GS_GACHA_REQUEST, new GsGachaRequest { keyInfo = key, gachaGroupIndex = 999 });
        Assert(server.Store.Find(account.uid)!.CashPoint == ten.currentCoinInfo.cashPoint && server.Store.Find(account.uid)!.CardIds.Count(x => x != 0) == cardsAfterTen, "Invalid pool cannot deduct currency or grant cards");
        var report = new { completedAt = DateTimeOffset.Now, passed = checks.Count, checks, saveDirectory = save,
            scope = "TCP integration: onboarding, announcements, simulated recharge, single/ten draws, replay rejection and persistence. No real payments." };
        File.WriteAllText(Path.Combine(root, "evidence/supported-features-test.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        System.Console.WriteLine($"PASS: {checks.Count} local feature integration checks.");
    }
}
