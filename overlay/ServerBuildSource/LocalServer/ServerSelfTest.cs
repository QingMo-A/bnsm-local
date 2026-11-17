extern alias game;
using System.Net.Sockets;
using System.Text.Json;
using System.Reflection;
using ProtoBuf;

internal static class ServerSelfTest
{
    public static async Task Run(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        var save = Path.Combine(root, "verification/local-server-test", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        void Assert(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
        using (var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables/PacketKeyTable.pb")))
        {
            typeof(game::DataTable).GetField("packetkeyDataTable", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(game::Singleton<game::DataTable>.Instance, Serializer.Deserialize<BscTable.PacketKeyDataTable>(input));
        }
        for (int length = 0; length < 32; length++)
        {
            ushort command = (ushort)(length * 17 + 1);
            var payload = Enumerable.Range(0, length).Select(i => (byte)(i * 19)).ToArray();
            var original = codec.Encode(command, payload, false);
            game::PacketEncrypt.EncryptPacket(original, command);
            if (!original.SequenceEqual(codec.Encode(command, payload, true))) throw new Exception($"Original client frame mismatch at payload length {length}");
        }
        checks.Add("32 frame encryption vectors match the recovered original client implementation");
        uint testUid = 0;
        uint testGold = 0, testStamina = 0;
        await using (var server = new LocalRuntime(root, save, 0, 0, 0))
        {
            async Task<(ushort Command, byte[] Payload)> Exchange(TcpClient client, ushort cmd, object? message, bool encrypted, bool fragmented = false)
            {
                var frame = codec.Encode(cmd, WireCodec.Serialize(message), encrypted);
                if (fragmented)
                {
                    for (int i = 0; i < frame.Length; i++) await client.GetStream().WriteAsync(frame.AsMemory(i, 1));
                }
                else await client.GetStream().WriteAsync(frame);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var result = codec.Decode((await WireCodec.ReadFrame(client.GetStream(), timeout.Token))!, encrypted);
                return (result.Command, result.Payload);
            }
            T Read<T>(byte[] payload) => (T)WireCodec.Deserialize(typeof(T), payload)!;
            using var directory = new TcpClient(); await directory.ConnectAsync("127.0.0.1", server.DirectoryPort);
            var dir = await Exchange(directory, (ushort)DirServerCommand.DS_GET_SERVER_LIST_REQUEST, new DsGetServerListRequest { openId = "offline-test" }, false, true);
            var list = Read<DsGetServerListOk>(dir.Payload);
            Assert(dir.Command == (ushort)DirServerCommand.DS_GET_SERVER_LIST_OK && list.list.Count == 1 && list.info.loginServerVirtualPort == server.LoginPort, "Fragmented directory request and server discovery");
            using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
            var version = await Exchange(login, (ushort)LoginServerCommand.LS_VERSION_CHECK_REQUEST, new LsVersionCheckRequest { checkVersion = 1 }, true);
            Assert(version.Command == (ushort)LoginServerCommand.LS_VERSION_CHECK_OK, "Encrypted version handshake");
            var logged = await Exchange(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest
            { userID = "offline-test", userPW = "local-only", deviceToken = "test-device", osType = DeviceOSType.DEVICE_PC }, true);
            Assert(logged.Command == (ushort)LoginServerCommand.LS_LOGIN_OK, "Local account login");
            var start = await Exchange(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true }, true);
            var account = Read<LsGameStartOk>(start.Payload); testUid = account.uid;
            Assert(account.uid > 0 && account.gameServerVirtualPort == server.GamePort, "Session token and game endpoint");
            var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
            using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
            var rejected = await Exchange(game, (ushort)GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,
                new GsGetUserGameInfoRequest { keyInfo = new SessionKeyInfo { uid = key.uid, loginSessionKey = "invalid" } }, true);
            Assert(rejected.Command == (ushort)GameServerCommand.GS_GET_USER_GAME_INFO_FAIL, "Invalid session is rejected");
            var sequence = await Exchange(game, (ushort)GameServerCommand.GS_GET_CURRENT_PACKET_SEQ_REQUEST,
                new GsGetCurrentPacketSequenceRequest { keyInfo = key }, true);
            Assert(sequence.Command == (ushort)GameServerCommand.GS_GET_CURRENT_PACKET_SEQ_OK &&
                Read<GsGetCurrentPacketSequenceOk>(sequence.Payload).currentPacketSequence == key.sequenceNo,
                "Real client startup sequence handshake uses the SEQ command alias");
            var inventory = await Exchange(game, (ushort)GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,
                new GsGetUserItemListRquest { keyInfo = key }, true);
            Assert(inventory.Command == (ushort)GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_OK &&
                Read<GsGetUserItemListOk>(inventory.Payload).totalDataCount == 0,
                "Real client inventory command decodes the legacy Rquest message alias");
            var info = await Exchange(game, (ushort)GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key }, true);
            var player = Read<GsGetUserGameInfoOk>(info.Payload);
            Assert(player.namecardInfo.commonInfo.level == 1 && player.coinInfo.gold == 10000, "Initial player profile");
            Assert(player.clearStageID == GameCatalog.TutorialEndStage && server.Catalog.FirstAdventureStage == player.clearStageID + 1,
                "Skipped original tutorial unlocks the first adventure stage (13)");
            var formations = await Exchange(game, (ushort)GameServerCommand.GS_GET_FORMATION_LIST_REQUEST,
                new GsGetFormationListRequest { keyInfo = key }, true);
            var usingFormations = await Exchange(game, (ushort)GameServerCommand.GS_GET_USING_FORMATION_REQUEST,
                new GsGetUsingFormationRequest { keyInfo = key }, true);
            Assert(formations.Command == (ushort)GameServerCommand.GS_GET_FORMATION_LIST_OK &&
                usingFormations.Command == (ushort)GameServerCommand.GS_GET_USING_FORMATION_OK,
                "Standard adventure deck page can fetch formation ownership and selection");
            Assert(player.serverTimeZone == 480 && Math.Abs(game::BSC_DEFINE.DotNetTime(player.serverTime) - DateTime.Now.Ticks) < TimeSpan.TicksPerMinute,
                "Recovered client time conversion and UTC offset in minutes");
            var cards = await Exchange(game, (ushort)GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST, new GsGetUserCardListRquest { keyInfo = key }, true);
            var cardList = Read<GsGetUserCardListOk>(cards.Payload);
            Assert(cardList.userCardInfoList.Count == 5 && cardList.userCardInfoList.All(c => server.Catalog.Cards.Any(x => x.Id == c.cardID)), "Starter cards use original configuration IDs");
            var deck = await Exchange(game, (ushort)GameServerCommand.GS_GET_USER_DECK_LIST_REQUEST, new GsGetUserDeckListRequest { keyInfo = key }, true);
            Assert(Read<GsGetUserDeckListOk>(deck.Payload).userDeckList.Any(x => x.cardSlot1 == 1), "Persisted local card deck");
            (GameServerCommand Command, object Message, Type Response)[] startup =
            [
                (GameServerCommand.GS_GET_CASH_BALANCE_REQUEST, new GsGetCashBalanceRequest { keyInfo = key }, typeof(GsGetCashBalanceOk)),
                (GameServerCommand.GS_GET_OPERATION_EVENT_DATA_REQUEST, new GsGetOperationEventDataRequest { keyInfo = key }, typeof(GsGetOperationEventDataOk)),
                (GameServerCommand.GS_GET_LIKE_YOU_INFO_REQUEST, new GsGetLikeYouInfoRequest { keyInfo = key }, typeof(GsGetLikeYouInfoOk)),
                (GameServerCommand.GS_GET_MY_GUILD_INFO_REQUEST, new GsGetMyGuildInfoRequest { keyInfo = key }, typeof(GsGetMyGuildInfoOk)),
                (GameServerCommand.GS_GET_FRIEND_LIST_REQUEST, new GsGetFriendListRequest { keyInfo = key }, typeof(GsGetFriendListOk)),
                (GameServerCommand.GS_GET_INVITE_FRIEND_LIST_REQUEST, new GsGetInviteFriendListRequest { keyInfo = key }, typeof(GsGetInviteFriendListOk)),
                (GameServerCommand.GS_GET_CLASS_RELIC_INFO_REQUEST, new GsGetClassRelicInfoRequest { keyInfo = key }, typeof(GsGetClassRelicInfoOk)),
                (GameServerCommand.GS_GET_INFINIT_ARENA_DECK_INFO_REQUEST, new GsGetInfiniteArenaUserDeckInfoRequest { keyInfo = key }, typeof(GsGetInfiniteArenaUserDeckInfoOk)),
                (GameServerCommand.GS_GET_WORLD_TOURNAMENT_DECK_INFO_REQUEST, new GsGetWorldTournamentDeckInfoRequest { keyInfo = key }, typeof(GsGetWorldTournamentDeckInfoOk)),
                (GameServerCommand.GS_GET_PROGRESSING_MISSION_LIST_REQUEST, new GsGetProgressingMissionListRequest { keyInfo = key }, typeof(GsGetProgressingMissionListOk)),
                (GameServerCommand.GS_GET_REWARDED_MISSION_LIST_REQUEST, new GsGetRewardedMissionListRequest { keyInfo = key }, typeof(GsGetRewardedMissionListOk)),
                (GameServerCommand.GS_GET_HARDMODE_BATTLE_COUNT_REQUEST, new GsGetHardModeBattleCountRequest { keyInfo = key }, typeof(GsGetHardModeBattleCountOk)),
                (GameServerCommand.GS_GET_MY_HELPER_LIST_REQUEST, new GsGetMyHelperListRequest { keyInfo = key }, typeof(GsGetMyHelperListOk)),
                (GameServerCommand.GS_GET_REWARD_LIST_REQUEST, new GsGetRewardInfoRequest { keyInfo = key }, typeof(GsGetRewardInfoOk)),
                (GameServerCommand.GS_GET_SYSTEM_NOTICE_REQUEST, new GsGetSystemNoticeRequest { keyInfo = key }, typeof(GsGetSystemNoticeOk)),
                (GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST, new GsCheckGameEntryRequest { keyInfo = key }, typeof(GsCheckGameEntryOk))
            ];
            foreach (var step in startup)
            {
                var response = await Exchange(game, (ushort)step.Command, step.Message, true);
                string expected = step.Command.ToString().Replace("_REQUEST", "_OK");
                Assert(((GameServerCommand)response.Command).ToString() == expected && WireCodec.Deserialize(step.Response, response.Payload) != null,
                    "Login startup read: " + step.Command);
            }
            var requestFrame = codec.Encode((ushort)GameServerCommand.GS_GET_SYSTEM_NOTICE_REQUEST,
                WireCodec.Serialize(new GsGetSystemNoticeRequest { keyInfo = key }), true);
            await game.GetStream().WriteAsync(requestFrame.Concat(requestFrame).ToArray());
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var first = codec.Decode((await WireCodec.ReadFrame(game.GetStream(), timeout.Token))!, true);
                var second = codec.Decode((await WireCodec.ReadFrame(game.GetStream(), timeout.Token))!, true);
                Assert(first.Command == (ushort)GameServerCommand.GS_GET_SYSTEM_NOTICE_OK && first.Command == second.Command
                    && second.Sequence == first.Sequence + 1, "Coalesced TCP requests and monotonically increasing response sequence");
            }
            uint stage = server.Catalog.FirstAdventureStage;
            var stageRule = server.Catalog.Stages[stage];
            testStamina = 100u - (uint)Math.Max(stageRule.ConsumeStamina, 0);
            var battle = await Exchange(game, (ushort)GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST, new GsNormalBattleStartRequest { keyInfo = key, stageid = stage }, true);
            var battleStart = Read<GsNormalBattleStartOk>(battle.Payload);
            Assert(battle.Command == (ushort)GameServerCommand.GS_NORMAL_BATTLE_START_OK && battleStart.stamina == testStamina && battleStart.waveProbIdList.Count > 0, "Normal PvE stage start with original enemy wave IDs");
            var endRequest = new GsNormalBattleEndConfirmRequest { keyInfo = key, stageId = stage, timeStamp = battleStart.timeStamp };
            var ended = await Exchange(game, (ushort)GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST, endRequest, true);
            var reward = Read<GsNormalBattleEndConfirmOk>(ended.Payload); testGold = reward.coinInfo.gold;
            Assert(reward.earnedExp == stageRule.RewardExp && reward.coinInfo.gold == 10000u + stageRule.Gold && reward.clearStageID == stage, "Local battle settlement and reward persistence");
            var replay = await Exchange(game, (ushort)GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST, endRequest, true);
            Assert(replay.Command == (ushort)GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_OK && replay.Payload.SequenceEqual(ended.Payload)
                && server.Store.Find(key.uid)!.Gold==testGold, "Duplicate battle settlement replays the exact receipt without granting rewards twice");
            var offers = await Exchange(game, (ushort)GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,
                new GsGetIngameShopSellingListRequest { keyInfo = key }, true);
            Assert(offers.Command == (ushort)GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_OK,
                "Victory offer refresh returns an empty list without disconnecting the client");
            var story = await Exchange(game, (ushort)GameServerCommand.GS_UPDATE_EPISODE_CUTSCENE_ID_REQUEST,
                new GsUpdateEpisodeCutSceneIDRequest { keyInfo = key, cutSceneID = 37 }, true);
            Assert(story.Command == (ushort)GameServerCommand.GS_UPDATE_EPISODE_CUTSCENE_ID_OK &&
                new PlayerStore(save, server.Catalog).Find(testUid)!.ClearEpisodeCutScene == 37,
                "Original episode skip/completion request persists without disconnecting");
            var interrupted = await Exchange(game, (ushort)GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
                new GsNormalBattleStartRequest { keyInfo = key, stageid = stage + 1 }, true);
            testStamina = Read<GsNormalBattleStartOk>(interrupted.Payload).stamina;
            await Exchange(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST,
                new LsGameStartRequest { serverId = 1, bAgreement = true }, true);
            var resumed = new PlayerStore(save, server.Catalog).Find(testUid)!;
            Assert(resumed.ActiveStage == null && resumed.ActiveBattleStamp == 0 && resumed.ClearStage == stage && resumed.Gold == testGold,
                "Re-entering abandons an interrupted fight and retains earned progress and currency");
        }
        var catalog = new GameCatalog(root); var restored = new PlayerStore(save, catalog).Find(testUid);
        Assert(restored != null && restored.Gold == testGold && restored.Stamina == testStamina, "Save reload retains player progress");
        string legacy = Path.Combine(save, "legacy"); Directory.CreateDirectory(legacy);
        var legacyPlayer = new LocalPlayer { Uid = 9001, Account = "legacy-test", ClearStage = 0, Gold = 12345,
            CashPoint = 678, CardIds = [20101, 30077], Deck = [2, 1] };
        File.WriteAllText(Path.Combine(legacy, "players.json"), JsonSerializer.Serialize(new[] { legacyPlayer }));
        var migrated = new PlayerStore(legacy, catalog).Find(9001)!;
        Assert(migrated.ClearStage == GameCatalog.TutorialEndStage && migrated.Gold == 12345 && migrated.CashPoint == 678 &&
            migrated.CardIds.SequenceEqual(legacyPlayer.CardIds) && migrated.Deck.SequenceEqual(legacyPlayer.Deck),
            "Legacy skipped-tutorial save unlocks adventure while retaining cards, deck and balances");
        Assert(Directory.GetFiles(legacy, "players.json.before-adventure-fix-*").Length == 1,
            "Legacy save migration keeps a recoverable backup");
        File.WriteAllText(Path.Combine(root, "evidence/local-server-self-test.json"), JsonSerializer.Serialize(new
        { passed = checks.Count, checks, saveDirectory = save, scope = "TCP protocol integration; Unity scene/game UI not covered" }, new JsonSerializerOptions { WriteIndented = true }));
        System.Console.WriteLine($"PASS: {checks.Count} local server integration checks.");
    }
}
