/// <summary>
/// 关卡星级与完美通关时间的持久化。
///
/// 客户端并不把星级存在本地：`PlayerInfoEx.GetStageAchieve` 一律从
/// `gameInfo.achieveInfo / hardAchieveInfo / legendAchieveInfo` 里读，
/// 索引是 `stage * 2`（星级）和 `stage * 2 + 1`（完美通关时间，byte 秒）。
/// 所以服务端必须在每次结算时记录、并在每次 `GS_GET_USER_GAME_INFO` 时下发，
/// 否则客户端每进一次游戏拿到的都是全 0，表现就是"打过的星星没了"。
///
/// 规则逐条移植自客户端：
///   * `BattleStage.GetNewAchieve`      —— 死亡卡数 0/1/其他 → 3/2/1 星
///   * `BattleStage.DoBattleEnd`        —— 完美通关时间只在"本次 0 死亡且战斗前已 3 星"时刷新，
///                                          且只在首次或更快时覆盖
///   * `PlayerInfoEx.SetClearStage`     —— 星级只升不降
/// </summary>
internal sealed partial class ProtocolRouter
{
    private static Dictionary<uint, LocalStageRecord> StageRecordsOf(LocalPlayer p, BscTable.GAME_DIFFICULTY difficulty) =>
        difficulty switch
        {
            BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD => p.HardStageRecords,
            BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => p.LegendStageRecords,
            _ => p.StageRecords,
        };

    /// <summary>`BattleStage.GetNewAchieve`：本场战斗能拿到的星级。`beforeStars` 是战斗前的星级。</summary>
    private byte StarsForStage(uint stageId, uint deadCards, byte beforeStars)
    {
        // 原逻辑：跳过教学的引导关在首次通关时直接给 3 星。
        uint skipTarget = (uint)catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.SKIP_TUTORIAL_TARGET_STAGE_ID);
        if (skipTarget != 0 && stageId == skipTarget && beforeStars == 0) return 3;
        return deadCards switch
        {
            0 => 3,
            1 => 2,
            _ => 1,
        };
    }

    /// <summary>
    /// 结算胜利后记录星级与完美通关时间。
    /// `clearTime` 取客户端 `PKBattleEndCommonData.clearTime`（= TotalBattleWaveTime 秒）。
    /// </summary>
    private void RecordStageResult(LocalPlayer p, BscTable.GAME_DIFFICULTY difficulty,
        uint stageId, uint deadCards, uint clearTime,bool dungeon=false)
    {
        var records = dungeon?DungeonRecordsOf(p,difficulty):StageRecordsOf(p, difficulty);
        if (!records.TryGetValue(stageId, out var record)) records[stageId] = record = new LocalStageRecord();
        // 完美时间用"战斗前"的星级判定，和客户端顺序一致。
        if (deadCards == 0 && record.Stars == 3)
        {
            byte time = (byte)clearTime; // 原客户端就是 (byte)TotalBattleWaveTime
            if (record.PerfectTime == 0 || record.PerfectTime > time) record.PerfectTime = time;
        }
        byte stars = StarsForStage(stageId, deadCards, record.Stars);
        if (stars > record.Stars) record.Stars = stars;
    }

    private static Dictionary<uint,LocalStageRecord> DungeonRecordsOf(LocalPlayer p,BscTable.GAME_DIFFICULTY d)=>d switch {
        BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD=>p.HardDungeonStageRecords,
        BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND=>p.LegendDungeonStageRecords,
        _=>p.DungeonStageRecords
    };

    /// <summary>把记录展开成客户端要的 `stage*2` / `stage*2+1` 字节数组。</summary>
    private static byte[] AchievementArray(Dictionary<uint, LocalStageRecord> records, int size)
    {
        var array = new byte[size];
        foreach (var (stageId, record) in records)
        {
            int index = (int)stageId * 2;
            if (index >= 0 && index < size) array[index] = record.Stars;
            if (index + 1 >= 0 && index + 1 < size) array[index + 1] = record.PerfectTime;
        }
        return array;
    }
}
