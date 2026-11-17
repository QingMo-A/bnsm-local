using System.Net.Sockets;
using System.Text.Json;

/// <summary>
/// 困难 / 传说难度：解锁链、每日次数、结算推进。
///
/// 断言里的具体数值都来自原表（evidence/difficulty-catalog.json）：
///   困难 167 关（id 13..179 连续，大关 3..19，每天 3 次，大关 3 需 11 级）
///   传说 61 关（id 稀疏 14..179，大关 3..19，每天 1 次）
///   解锁：困难需先通关普通大关 4（末关 29）；传说需先通关困难大关 3（末关 18）
/// </summary>
internal static class DifficultyTest
{
    public static async Task Run(string root)
    {
        string save = Path.Combine(root, "verification/difficulty", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Assert(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        await using var server = new LocalRuntime(root, save, 0, 0, 0);
        var catalog = server.Catalog;

        // ---------- A. 原表事实 ----------
        Assert(catalog.HardStages.Count == 167, "Hard table exposes 167 playable stages");
        Assert(catalog.LegendStages.Count == 61 && catalog.LegendStageRows.Count == 61,
            "Legend table drops 109 all-zero placeholder rows and keeps 61 playable stages");
        Assert(catalog.NormalFieldStages[3].SequenceEqual(new uint[] { 13, 14, 15, 16, 17, 18 }),
            "Normal field 3 is the contiguous range 13..18");
        Assert(catalog.LegendStageRows.All(x => x.EpSubId != 0),
            "Every legend stage has a non-zero EpSubId, which GetNextLegendStageID relies on");
        Assert(StageUnlock.RequireLevelToUnlockField(catalog, 3, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD) == 11,
            "Hard field 3 requires player level 11");

        // ---------- B. 解锁语义（与客户端 GetFieldOpen / GetLastEnableStageId 对齐） ----------
        LocalPlayer Fresh(byte level, uint normal, uint hard, uint legend) => new()
        {
            Level = level, ClearStage = normal, ClearHardStage = hard, ClearLegendStage = legend
        };
        bool Can(LocalPlayer p, BscTable.GAME_DIFFICULTY d, uint stage) => StageUnlock.CanEnter(catalog, p, d, stage);

        var newbie = Fresh(1, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage);
        Assert(Can(newbie, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 13), "Fresh account may enter normal stage 13");
        Assert(!Can(newbie, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 14), "Fresh account cannot skip ahead to normal stage 14");
        Assert(!Can(newbie, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13),
            "Hard is sealed until normal field 4 is cleared");
        Assert(!Can(newbie, BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 14),
            "Legend is sealed until hard field 3 is cleared");

        var hardReady = Fresh(11, 29, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage);
        Assert(Can(hardReady, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13), "Clearing normal field 4 opens hard field 3");
        Assert(!Can(hardReady, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 14),
            "Hard still advances one stage at a time inside the opened field");
        Assert(!Can(hardReady, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 19), "Hard field 4 stays sealed until hard field 3 is cleared");
        Assert(StageUnlock.LastEnableStage(catalog, Fresh(11, 29, 18, 12), BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD) == 19,
            "Clearing hard 18 opens hard field 4 up to stage 19");

        var underLeveled = Fresh(1, 29, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage);
        Assert(!Can(underLeveled, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13),
            "Hard field 3 still rejects a level-1 account after normal field 4");

        var legendReady = Fresh(11, 29, 18, GameCatalog.TutorialEndStage);
        Assert(Can(legendReady, BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 14), "Clearing hard field 3 opens legend field 3");
        Assert(!Can(Fresh(11, 29, 17, GameCatalog.TutorialEndStage), BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 14),
            "One stage short of hard field 3 keeps legend sealed");

        Assert(StageUnlock.NextLegendStage(catalog, 12) == 14, "Legend progression starts at 14 after the tutorial end");
        Assert(StageUnlock.NextLegendStage(catalog, 14) == 16, "Sparse legend ids advance 14 -> 16, never to a missing 15");
        Assert(StageUnlock.NextLegendStage(catalog, 16) == 18, "Legend stays inside field 3 after 16");
        Assert(StageUnlock.NextLegendStage(catalog, 18) == 21, "Legend crosses into field 4 after the last stage of field 3");

        var cleared14 = Fresh(11, 29, 18, 14);
        Assert(Can(cleared14, BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 16), "Legend 16 unlocks after clearing 14");
        Assert(!Can(cleared14, BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 18), "Legend 18 stays locked until 16 is cleared");

        var endgame = Fresh(11, 179, 179, 179);
        Assert(StageUnlock.LastEnableStage(catalog, endgame, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL) == 179,
            "Normal progression caps at the last original stage 179");
        Assert(!Can(endgame, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 180), "Normal stage 180 does not exist and is rejected");
        Assert(Can(newbie, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, 12),
            "Tutorial stages stay replayable from the tutorial-end clear value");

        // ---------- C. 真实报文 ----------
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
            new LsLoginRequest { userID = "difficulty-test", userPW = "local-only", osType = DeviceOSType.DEVICE_PC });
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
        void Prime(byte level, uint normal, uint hard, uint legend)
        {
            p.Level = level;
            p.Exp = (uint)catalog.PlayerStates.Single(x => x.Level == level).Exp;
            p.ClearStage = normal; p.ClearHardStage = hard; p.ClearLegendStage = legend;
            p.ActiveStage = null; p.ActiveDifficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL; p.ActiveBattleStamp = 0;
            p.HardBattleCounts.Clear(); p.LegendBattleCounts.Clear();
        }
        void FillStamina() { p.Stamina = catalog.StaminaMax(p.Level); p.StaminaStamp = ProtocolRouter.Now; }
        async Task<GsNormalBattleStartOk> Start(BscTable.GAME_DIFFICULTY difficulty, uint stage)
        {
            FillStamina();
            try
            {
                return Read<GsNormalBattleStartOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
                    new GsNormalBattleStartRequest { keyInfo = key, stageid = stage, difficulty = difficulty }));
            }
            catch (Exception e) { throw new Exception($"[start {difficulty} {stage}] {e.Message}", e); }
        }
        async Task<GsNormalBattleEndConfirmOk> Win(BscTable.GAME_DIFFICULTY difficulty, uint stage, GsNormalBattleStartOk battle) =>
            Read<GsNormalBattleEndConfirmOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,
                new GsNormalBattleEndConfirmRequest
                {
                    keyInfo = key, stageId = stage, difficulty = difficulty,
                    endType = NormalBattleEndType.NORMAL_BATTLE_SUCCESS, timeStamp = battle.timeStamp
                }));

        // 困难：大关 3 每天 3 次
        Prime(11, 29, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage);
        FillStamina();
        uint staminaBefore = p.Stamina;
        var hardBattle = await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13);
        Assert(hardBattle.waveProbIdList.Count > 0, "Hard stage start returns the original enemy wave ids");
        Assert(p.Stamina == staminaBefore - 20, "Hard stage entry consumes the original 20 stamina");
        var hardWin = await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13, hardBattle);
        Assert(hardWin.clearStageID == 13 && p.ClearHardStage == 13, "Hard clear advances clearHardStageID without touching normal progress");
        var info = Read<GsGetUserGameInfoOk>(await Request(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,
            new GsGetUserGameInfoRequest { keyInfo = key }));
        Assert(info.clearHardStageID == 13 && info.clearStageID == 29,
            "Game info reports hard and normal progress independently");
        Assert(info.hardAchieveInfo.Length >= 180 * 2 && info.legendAchieveInfo.Length >= 180 * 2,
            "Achievement arrays cover the highest original stage id instead of a fixed 256 bytes");

        await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13));
        await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13));
        Assert(p.HardBattleCounts[(uint)13] == 3, "Three hard clears are counted for the daily limit");
        FillStamina();
        uint beforeReject = p.Stamina;
        await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
            new GsNormalBattleStartRequest { keyInfo = key, stageid = 13, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD }, false);
        Assert(p.Stamina == beforeReject && p.ActiveStage == null,
            "Fourth hard entry is rejected by DailyLimit without charging stamina");
        var counts = Read<GsGetHardModeBattleCountOk>(await Request(GameServerCommand.GS_GET_HARDMODE_BATTLE_COUNT_REQUEST,
            new GsGetHardModeBattleCountRequest { keyInfo = key }));
        Assert(counts.stageBattleCount.Count(x => x.stageid == 13 && x.battleCount == 3) == 1,
            "Hard mode battle-count report returns the real used entries");

        // VIP 购买当日次数：价格取原 ExpenseWeightTable 档位，额度取 VipData
        var vip1 = catalog.Vip.Single(x => x.VipLv == 1);
        p.VipExp = (uint)vip1.LvUpNeedVipExp;
        p.CashPoint = 10000;
        Assert(vip1.HardStageMaxResetCount > 0 && catalog.Vip.Single(x => x.VipLv == 0).HardStageMaxResetCount == 0,
            "Only VIP 1 and above can buy hard-stage entry resets");
        uint cashBefore = p.CashPoint;
        uint resetCost = (uint)catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_HARD_STAGE, 1)!.Value;
        var resetOk = Read<GsResetHardModeBattleVIPEntranceCountOk>(await Request(
            GameServerCommand.GS_RESET_HARDMODE_BATTLE_VIP_ENTRANCE_COUNT_REQUEST,
            new GsResetHardModeBattleVIPEntranceCountRequest { keyInfo = key, stageId = 13, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD }));
        Assert(resetOk.resetCount == 1 && resetOk.nowCoinInfo.cashPoint == cashBefore - resetCost,
            "Buying an entry reset charges the original tier price and returns the new reset count");
        Assert(p.HardBattleCounts.GetValueOrDefault(13u) == 0, "Buying a reset clears the used entries for that stage");
        await Request(GameServerCommand.GS_RESET_HARDMODE_BATTLE_VIP_ENTRANCE_COUNT_REQUEST,
            new GsResetHardModeBattleVIPEntranceCountRequest { keyInfo = key, stageId = 13, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD }, false);
        Assert(p.HardBattleResetCounts.GetValueOrDefault(13u) == 1 && p.CashPoint == cashBefore - resetCost,
            "A stage with entries left cannot be reset and is not charged twice");
        for (int i = 0; i < 3; i++)
            await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13, await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13));
        Assert(p.HardBattleCounts[(uint)13] == 3, "The bought reset only restores the original daily allowance");
        await Request(GameServerCommand.GS_RESET_HARDMODE_BATTLE_VIP_ENTRANCE_COUNT_REQUEST,
            new GsResetHardModeBattleVIPEntranceCountRequest { keyInfo = key, stageId = 13, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD }, false);
        Assert(p.HardBattleResetCounts.GetValueOrDefault(13u) == 1,
            "VIP 1 allows exactly one reset per stage per day");

        // 等级门槛：普通大关 4 已通也不行
        Prime(1, 29, GameCatalog.TutorialEndStage, GameCatalog.TutorialEndStage);
        FillStamina();
        uint lowLevelStamina = p.Stamina;
        await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
            new GsNormalBattleStartRequest { keyInfo = key, stageid = 13, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD }, false);
        Assert(p.Stamina == lowLevelStamina && p.ActiveStage == null, "Hard entry is rejected for a level-1 account without charging stamina");

        // 传说：大关 3 每天 1 次，且 id 稀疏
        Prime(11, 29, 18, GameCatalog.TutorialEndStage);
        var legendBattle = await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 14);
        Assert(legendBattle.waveProbIdList.Count > 0, "Legend stage start returns the original enemy wave ids");
        var legendWin = await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 14, legendBattle);
        Assert(legendWin.clearStageID == 14 && p.ClearLegendStage == 14 && p.ClearHardStage == 18,
            "Legend clear advances clearLegendStageID only");
        await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
            new GsNormalBattleStartRequest { keyInfo = key, stageid = 14, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND }, false);
        Assert(p.ActiveStage == null, "Legend stage 14 is limited to one entry per day");
        await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,
            new GsNormalBattleStartRequest { keyInfo = key, stageid = 15, difficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND }, false);
        Assert(p.ActiveStage == null, "Legend stage 15 does not exist in the original table and is rejected");
        var nextLegend = await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 16);
        Assert(nextLegend.waveProbIdList.Count > 0,
            "Legend advances to 16 after clearing the sparse stage 14");
        var legend16 = await Win(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, 16, nextLegend);
        Assert(legend16.clearStageID == 16, "Legend clear advances to the sparse stage 16");

        // 每日重置：次数清零后同一关可以再打
        p.ExchangeDay--;
        await Request(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key });
        Assert(p.HardBattleCounts.Count == 0 && p.LegendBattleCounts.Count == 0,
            "Difficulty entry counters reset at the shared UTC+8 day boundary");
        Assert(await Start(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, 13) is { waveProbIdList.Count: > 0 },
            "Hard entries become available again on the next day");

        System.Console.WriteLine($"PASS: {checks.Count} hard/legend difficulty checks.");
    }
}
