/// <summary>
/// 困难 / 传说难度的每日次数。
///
/// 数值全部来自原表 `BattleStageTable_Hard` / `BattleStageTable_Legend`：
///   ConsumeStamina 进入体力、Gold 金币、RewardExp 人物经验、CardRewardExp 卡牌经验、
///   DailyLimit 每日可打次数、RequirePcLevel 人物等级门槛。
///
/// 次数公式照抄客户端 `BattleStageLink.RemainBattleCount`：
///   `max(DailyLimit - GetBattleCount(diff, IsDungeon, Id), 0)`
/// 解锁规则不在这里，见 StageUnlock.cs。
/// 每日次数与体力购买共用同一个 UTC+8 日界（见 RefreshTime）。
/// </summary>
internal sealed partial class ProtocolRouter
{
    private static Dictionary<uint, uint> BattleCountsOf(LocalPlayer p, BscTable.GAME_DIFFICULTY difficulty,bool dungeon=false) =>
        dungeon ? p.HardDungeonBattleCounts : difficulty == BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND ? p.LegendBattleCounts : p.HardBattleCounts;

    private static Dictionary<uint, uint> ResetCountsOf(LocalPlayer p, BscTable.GAME_DIFFICULTY difficulty,bool dungeon=false) =>
        dungeon ? p.HardDungeonResetCounts : difficulty == BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND ? p.LegendBattleResetCounts : p.HardBattleResetCounts;

    /// <summary>
    /// VIP 购买当日副本次数（客户端 `BscEnterStageNormal.OnBuyChanceOK`）。
    ///
    /// 前置：难度只有困难/传说、该关当日次数已用尽、且 VIP 还留有重置额度
    /// （`VipData.HardStageMaxResetCount` / `LegendStageMaxResetCount`）。
    /// 价格不是固定值，而是 `ExpenseWeightTable` 里 `EXPENSE_RESET_HARD_STAGE` /
    /// `EXPENSE_RESET_LEGEND_STAGE` 按"第几次重置"递增的档位，货币是点券。
    /// </summary>
    private Reply ResetHardModeEntry(LocalPlayer p, GsResetHardModeBattleVIPEntranceCountRequest request,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        var difficulty = request.difficulty;
        if (difficulty is not (BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD or BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND) ||
            (request.dungeonFlag && difficulty!=BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD))
            return fail("unsupported difficulty for entry reset", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (!catalog.BattleStages(difficulty,request.dungeonFlag).TryGetValue(request.stageId, out var stage) ||
            (request.dungeonFlag ? !CanEnterDungeon(p,difficulty,stage) : !StageUnlock.CanEnter(catalog,p,difficulty,request.stageId)))
            return fail("unknown stage for this difficulty", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        var vip = catalog.VipFor(p.VipExp);
        uint maxReset = (uint)Math.Max(0, request.dungeonFlag ? vip.HardDungeonMaxResetCount : difficulty == BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND
            ? vip.LegendStageMaxResetCount : vip.HardStageMaxResetCount);
        var used = ResetCountsOf(p, difficulty,request.dungeonFlag);
        uint already = used.GetValueOrDefault(request.stageId);
        if (already >= maxReset)
            return fail("no VIP reset left for this stage", ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if (stage.DailyLimit<=0 || RemainingBattleCount(p, stage, difficulty,request.dungeonFlag) > 0)
            return fail("stage still has entries left today", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        var expense = catalog.Expenses.FindItem(request.dungeonFlag ? BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_HARD_DUNGEON : difficulty == BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND
            ? BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_LEGEND_STAGE
            : BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_HARD_STAGE, (int)already + 1);
        uint cost = (uint)Math.Max(expense?.Value ?? 0, 0);
        if (p.CashPoint < cost) return fail("not enough diamonds", ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        p.CashPoint -= cost;
        used[request.stageId] = already + 1;
        BattleCountsOf(p, difficulty,request.dungeonFlag)[request.stageId] = 0;
        store.Save();
        return ok(new GsResetHardModeBattleVIPEntranceCountOk
        { resetCount = used[request.stageId], nowCoinInfo = Coins(p) });
    }

    /// <summary>今天该难度这一关还能打几次。DailyLimit=0 视为不限次（普通难度就是 0）。</summary>
    private static uint RemainingBattleCount(LocalPlayer p, BscTable.BattleStageLink stage,
        BscTable.GAME_DIFFICULTY difficulty,bool dungeon=false)
    {
        if (stage.DailyLimit <= 0) return uint.MaxValue;
        uint used = BattleCountsOf(p, difficulty,dungeon).GetValueOrDefault((uint)stage.Id);
        return (uint)Math.Max(stage.DailyLimit - used, 0);
    }
}
