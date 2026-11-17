/// <summary>
/// 关卡解锁判定，逐条移植客户端 `PlayerInfoEx` / `NormalFieldmapHandler` / `DataTable` 的原逻辑。
///
/// 移植来源（decomp_v2，行号见注释）：
///   * `PlayerInfoEx.GetClearStageID`         —— 三个难度各自读 clearStageID / clearHardStageID / clearLegendStageID
///   * `PlayerInfoEx.GetNextLegendStageID`    —— 传说关卡的 id 是稀疏的，必须按"下一个存在的关"推进
///   * `PlayerInfoEx.GetLastEnableStageId`    —— 玩家当前能进到的最大关卡（先算候选，再按大关是否开放回退）
///   * `PlayerInfoEx.GetFieldOpen`            —— 大关开放条件（内容开关 + 上一大关已通 + 等级 + 模式解锁）
///   * `PlayerInfoEx.GetFieldClear`           —— 大关已通 = 该难度通关数 >= 该大关最后一关
///   * `DataTable.GetRequirPCLevelToUnlockField` / `GetFirstStage` / `GetLastStage`
///
/// 关键事实（原表实测，见 evidence/difficulty-catalog.json）：
///   - 普通 id 10..179 连续，大关 2..19（另有 1000 号特殊大关 450..469）
///   - 困难 id 13..179 连续，大关 3..19（没有大关 2）
///   - **传说 id 稀疏**（61 关散布在 14..179），所以绝不能用 clear+1 推进
///   - 困难要求先通关普通大关 4（末关 29），传说要求先通关困难大关 3（末关 18）
/// </summary>
internal static class StageUnlock
{
    public static uint ClearStageOf(LocalPlayer player, BscTable.GAME_DIFFICULTY difficulty) => difficulty switch
    {
        BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD => player.ClearHardStage,
        BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => player.ClearLegendStage,
        _ => player.ClearStage,
    };

    public static void SetClearStage(LocalPlayer player, BscTable.GAME_DIFFICULTY difficulty, uint stage)
    {
        switch (difficulty)
        {
            case BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD:
                player.ClearHardStage = Math.Max(player.ClearHardStage, stage); break;
            case BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND:
                player.ClearLegendStage = Math.Max(player.ClearLegendStage, stage); break;
            default:
                player.ClearStage = Math.Max(player.ClearStage, stage); break;
        }
    }

    /// <summary>`DataTable.GetFirstStage(fieldId)`：普通表里该大关的第一关。</summary>
    public static uint FirstStageOfField(GameCatalog catalog, int fieldId) =>
        catalog.NormalFieldStages.TryGetValue(fieldId, out var stages) && stages.Count > 0 ? stages[0] : 0;

    /// <summary>`DataTable.GetLastStage(fieldId)`：普通表里该大关的最后一关。</summary>
    public static uint LastStageOfField(GameCatalog catalog, int fieldId) =>
        catalog.NormalFieldStages.TryGetValue(fieldId, out var stages) && stages.Count > 0 ? stages[^1] : 0;

    /// <summary>`PlayerInfoEx.GetFieldClear`：该难度通关进度是否已过这一大关的最后一关。</summary>
    public static bool FieldClear(GameCatalog catalog, LocalPlayer player,
        BscTable.GAME_DIFFICULTY difficulty, int fieldId) =>
        ClearStageOf(player, difficulty) >= LastStageOfField(catalog, fieldId);

    /// <summary>`DataTable.GetRequirPCLevelToUnlockField`：该难度下这一大关第一关的等级门槛。</summary>
    public static int RequireLevelToUnlockField(GameCatalog catalog, int fieldId,
        BscTable.GAME_DIFFICULTY difficulty) =>
        catalog.StagesFor(difficulty).TryGetValue(FirstStageOfField(catalog, fieldId), out var link)
            ? link.RequirePcLevel : 0;

    private static BscTable.CONTENT_OPEN_TYPE ContentTypeOf(BscTable.GAME_DIFFICULTY difficulty) => difficulty switch
    {
        BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD => BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_HARD,
        BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_LEGEND,
        _ => BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_NORMAL,
    };

    /// <summary>`PlayerInfoEx.GetFieldOpen`。</summary>
    public static bool FieldOpen(GameCatalog catalog, LocalPlayer player,
        BscTable.GAME_DIFFICULTY difficulty, int fieldId)
    {
        // 客户端用 `fieldId - 1` 去查普通表的 __fieldMap，这里保持一致（不是笔误）。
        if (!catalog.NormalFieldStages.ContainsKey(fieldId - 1)) return false;
        if (fieldId > catalog.ContentValue(ContentTypeOf(difficulty))) return false;
        if (!FieldClear(catalog, player, difficulty, fieldId - 1)) return false;
        if (player.Level < RequireLevelToUnlockField(catalog, fieldId, difficulty)) return false;
        return difficulty switch
        {
            BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL => true,
            BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD =>
                FieldClear(catalog, player, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, GameCatalog.NormalFieldToUnlockHard) &&
                FieldClear(catalog, player, BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, fieldId),
            BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND =>
                FieldClear(catalog, player, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, GameCatalog.HardFieldToUnlockLegend) &&
                FieldClear(catalog, player, BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, fieldId),
            _ => false,
        };
    }

    /// <summary>`DataTable.GetFieldIdByStageId`：只在指定难度表里查，查不到时回落到 教学结束关+1。</summary>
    private static int FieldOfStage(GameCatalog catalog, uint stageId, BscTable.GAME_DIFFICULTY difficulty) =>
        catalog.StagesFor(difficulty).TryGetValue(stageId, out var link) ? link.FieldId
        : catalog.StagesFor(difficulty).TryGetValue(GameCatalog.TutorialEndStage + 1, out var fallback) ? fallback.FieldId
        : 0;

    /// <summary>`PlayerInfoEx.GetNextLegendStageID`：传说关卡 id 稀疏，按"同大关内下一个 EpSubId 非零的关"推进。</summary>
    public static uint NextLegendStage(GameCatalog catalog, uint currentStageId)
    {
        if (currentStageId == GameCatalog.TutorialEndStage) return currentStageId + 2;
        uint result = currentStageId;
        int field = FieldOfStage(catalog, currentStageId, BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND);
        while (true)
        {
            // 客户端按表内 List 顺序遍历，这里同样不排序。
            var stages = catalog.LegendStageRows.Where(x => x.FieldId == field).ToList();
            if (stages.Count == 0) break;
            foreach (var item in stages)
                if (item.EpSubId != 0 && item.Id > currentStageId) { result = (uint)item.Id; break; }
            if (result != currentStageId) break;
            field++;
        }
        return result;
    }

    /// <summary>
    /// `PlayerInfoEx.GetLastEnableStageId`：玩家在该难度当前能进到的最大关卡（含已通关可复刷的）。
    /// 返回 -1 表示一关都进不去（客户端同样返回 -1）。
    /// </summary>
    public static int LastEnableStage(GameCatalog catalog, LocalPlayer player, BscTable.GAME_DIFFICULTY difficulty)
    {
        uint clear = ClearStageOf(player, difficulty);
        int candidate = difficulty switch
        {
            BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => (int)NextLegendStage(catalog, clear),
            _ => (int)clear + 1,
        };
        candidate = Math.Clamp(candidate, 0, (int)catalog.MaxPveStage);
        while (candidate >= 0)
        {
            if (catalog.Stages.TryGetValue((uint)candidate, out var link) &&
                FieldOpen(catalog, player, difficulty, link.FieldId)) break;
            candidate--;
        }
        return candidate;
    }

    /// <summary>服务端入口校验：该关当前是否允许进入（客户端可点 = 服务端必须放行）。</summary>
    public static bool CanEnter(GameCatalog catalog, LocalPlayer player,
        BscTable.GAME_DIFFICULTY difficulty, uint stageId)
    {
        int last = LastEnableStage(catalog, player, difficulty);
        return last >= 0 && stageId <= (uint)last;
    }
}
