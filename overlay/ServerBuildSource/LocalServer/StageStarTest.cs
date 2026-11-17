using System.Net.Sockets;
using System.Text.Json;

/// <summary>
/// 关卡星级与完美通关时间的持久化。
///
/// 这里防的是一个真实的用户可见缺陷：客户端只从 `gameInfo.achieveInfo / hardAchieveInfo /
/// legendAchieveInfo` 读星级（索引 `stage*2`），服务端早先每次登录都返回全 0，
/// 于是"打出来的星星，下次进游戏就没了"。
///
/// 星级规则照抄客户端 `BattleStage.GetNewAchieve`：死亡卡数 0/1/其他 → 3/2/1 星，
/// 且只升不降。完美通关时间只在"本次 0 死亡且战斗前已 3 星"时刷新，并取更快的值。
/// </summary>
internal static class StageStarTest
{
    public static async Task Run(string root)
    {
        string save = Path.Combine(root, "verification/stage-stars", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Assert(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        await using var server = new LocalRuntime(root, save, 0, 0, 0);
        var catalog = server.Catalog;

        using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
        async Task<(ushort Command, byte[] Payload)> Send(TcpClient socket, ushort command, object message)
        {
            await socket.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(message), true));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = codec.Decode((await WireCodec.ReadFrame(socket.GetStream(), timeout.Token))!, true);
            return (response.Command, response.Payload);
        }
        T Read<T>(byte[] bytes) => (T)WireCodec.Deserialize(typeof(T), bytes)!;
        await Send(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST,
            new LsLoginRequest { userID = "stage-star-test", userPW = "local-only", osType = DeviceOSType.DEVICE_PC });
        var account = Read<LsGameStartOk>((await Send(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST,
            new LsGameStartRequest { serverId = 1, bAgreement = true })).Payload);
        var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
        using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
        var p = server.Store.Find(account.uid)!;
        async Task<byte[]> Request(GameServerCommand command, object message, bool success = true)
        {
            key.sequenceNo++;
            var response = await Send(game, (ushort)command, message);
            string expected = command.ToString().Replace("_REQUEST", success ? "_OK" : "_FAIL");
            if (((GameServerCommand)response.Command).ToString() != expected)
            {
                string why = string.Empty;
                try { why = Read<FailPacket>(response.Payload).message; } catch { }
                throw new Exception($"Expected {expected}, got {(GameServerCommand)response.Command} ({why})");
            }
            return response.Payload;
        }
        async Task<GsGetUserGameInfoOk> Info() => Read<GsGetUserGameInfoOk>(await Request(
            GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key }));
        void FillStamina() { p.Stamina = catalog.StaminaMax(p.Level); p.StaminaStamp = ProtocolRouter.Now; }
        async Task<GsNormalBattleStartOk> Start(BscTable.GAME_DIFFICULTY difficulty, uint stage)
        {
            FillStamina();
            return Read<GsNormalBattleStartOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
                new GsNormalBattleStartRequest { keyInfo = key, stageid = stage, difficulty = difficulty }));
        }
        async Task<GsNormalBattleEndConfirmOk> Settle(BscTable.GAME_DIFFICULTY difficulty, uint stage,
            GsNormalBattleStartOk battle, uint deadCards = 0, uint clearTime = 0,
            NormalBattleEndType endType = NormalBattleEndType.NORMAL_BATTLE_SUCCESS)
        {
            var end = new GsNormalBattleEndConfirmRequest
            {
                keyInfo = key, stageId = stage, difficulty = difficulty, endType = endType,
                timeStamp = battle.timeStamp, cardDieCount = deadCards
            };
            if (clearTime > 0) end.battleEndSyncData = new PKBattleEndCommonData { clearTime = clearTime };
            return Read<GsNormalBattleEndConfirmOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST, end));
        }
        const int Index = 2; // stage * 2 的乘数

        // ---------- A. 首通三题：星级真的被记下来了 ----------
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13));
        var afterFirst = await Info();
        Assert(afterFirst.achieveInfo[13 * Index] == 3, "A clean first clear awards three stars");
        Assert(server.Store.Find(p.Uid)!.StageRecords[13].Stars == 3, "Three stars are written into the saved profile");

        // 关键：模拟"下次再进游戏"——从磁盘重新读存档，星级必须还在。
        var reloaded = new PlayerStore(save, catalog).Find(p.Uid)!;
        Assert(reloaded.StageRecords.TryGetValue(13, out var saved) && saved.Stars == 3,
            "Stars survive a profile reload instead of resetting on the next login");
        Assert((await Info()).achieveInfo[13 * Index] == 3, "A fresh game-info read still reports the saved stars");

        // ---------- B. 死亡卡数决定星级 ----------
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 14, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 14), deadCards: 1);
        Assert((await Info()).achieveInfo[14 * Index] == 2, "Losing one card in the battle yields two stars");
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 15, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 15), deadCards: 3);
        Assert((await Info()).achieveInfo[15 * Index] == 1, "Losing more cards yields a single star");

        // ---------- C. 星级只升不降 ----------
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13), deadCards: 2);
        Assert((await Info()).achieveInfo[13 * Index] == 3, "Replaying a three-star stage worse never lowers the record");

        // ---------- D. 完美通关时间 ----------
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13), clearTime: 30);
        Assert((await Info()).achieveInfo[13 * Index + 1] == 30, "A three-star stage records the perfect clear time");
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13), clearTime: 50);
        Assert((await Info()).achieveInfo[13 * Index + 1] == 30, "A slower clear does not overwrite the best time");
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13), clearTime: 12);
        Assert((await Info()).achieveInfo[13 * Index + 1] == 12, "A faster clear replaces the best time");
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 14, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 14), deadCards: 1, clearTime: 20);
        Assert((await Info()).achieveInfo[14 * Index + 1] == 0, "A two-star clear never records a perfect time");

        // ---------- E. 失败不记星 ----------
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 16,
            await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 16), endType: NormalBattleEndType.NORMAL_BATTLE_FAIL);
        Assert((await Info()).achieveInfo[16 * Index] == 0, "A failed battle records no stars");

        // ---------- F. 三个难度的星级互不干扰 ----------
        p.Level = 11;
        p.Exp = (uint)catalog.PlayerStates.Single(x => x.Level == 11).Exp;
        p.ClearStage = 29;
        p.ClearHardStage = GameCatalog.TutorialEndStage;
        p.ActiveStage = null;
        await Settle(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13), deadCards: 1);
        var mixed = await Info();
        Assert(mixed.hardAchieveInfo[13 * Index] == 2 && mixed.achieveInfo[13 * Index] == 3,
            "Hard difficulty keeps its own star record separate from normal");

        var finalReload = new PlayerStore(save, catalog).Find(p.Uid)!;
        Assert(finalReload.HardStageRecords.TryGetValue(13, out var hardSaved) && hardSaved.Stars == 2 &&
               finalReload.StageRecords[13].Stars == 3 && finalReload.StageRecords[13].PerfectTime == 12,
            "Every difficulty's stars and best time persist together across a reload");

        // ---------- G. 老存档迁移：有通关进度、没星级记录 ----------
        string legacyDir = Path.Combine(root, "verification/stage-stars", Guid.NewGuid().ToString("N"), "legacy");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "players.json"),
            """[{"Uid":2001,"Account":"legacy","Level":6,"ClearStage":18,"ClearHardStage":12,"ClearLegendStage":12,"CardIds":[],"Deck":[]}]""");
        var legacyStore = new PlayerStore(legacyDir, catalog);
        Assert(legacyStore.BackfillMissingStageStars() == 1, "The legacy profile is picked up by the one-time backfill");
        var migrated = legacyStore.Find(2001)!;
        Assert(migrated.StageRecords.Keys.OrderBy(x => x).SequenceEqual(new uint[] { 13, 14, 15, 16, 17, 18 }),
            "A legacy save backfills one star for exactly the stages it had cleared");
        Assert(!migrated.StageRecords.ContainsKey(12) && !migrated.HardStageRecords.Any(),
            "Tutorial stages and untouched difficulties gain no star record");
        Assert(Directory.GetFiles(legacyDir, "players.json.before-stage-stars-*").Length == 1,
            "The legacy save is backed up before stars are backfilled");
        var withoutStarRecords = new PlayerStore(legacyDir, catalog);
        Assert(withoutStarRecords.BackfillMissingStageStars() == 0 && withoutStarRecords.Find(2001)!.StageRecords.Count == 6,
            "A second start does not backfill the same profile twice");

        System.Console.WriteLine($"PASS: {checks.Count} stage star and perfect-time checks.");
    }
}
