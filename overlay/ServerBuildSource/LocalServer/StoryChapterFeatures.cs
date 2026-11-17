internal sealed partial class ProtocolRouter
{
    private const BscTable.GAME_DIFFICULTY StoryDifficulty = BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL;

    private bool HasStoryHero(LocalPlayer player, int ancestor) =>
        catalog.Cards.Any(card => card.EvolveFirstAncestor == ancestor && player.CardIds.Contains((uint)card.Id));

    private bool StoryUnlocked(LocalPlayer player, LocalStoryChapter stage) =>
        player.Level >= stage.Level &&
        (stage.PreviousStage == 0 || player.StoryClears.Contains((uint)stage.PreviousStage)) &&
        (stage.RequiredHero == 0 || HasStoryHero(player, stage.RequiredHero));

    private static bool StorySweepUnlocked(LocalPlayer player, LocalStoryChapter stage) =>
        stage.DailySweeps > 0 && player.StoryClears.Contains((uint)stage.Id) &&
        player.StoryClears.Contains((uint)stage.SweepAfterStage);

    private static uint StorySweepsLeft(LocalPlayer player, LocalStoryChapter stage, ulong day)
    {
        uint used = day > player.StorySweepDay ? 0 : player.StorySweeps.GetValueOrDefault((uint)stage.Id);
        return (uint)stage.DailySweeps - Math.Min((uint)stage.DailySweeps, used);
    }

    private static void UseStoryRepeatRewards(LocalPlayer player, uint stageId, ulong day, uint count)
    {
        if (day > player.StorySweepDay)
        {
            player.StorySweepDay = day;
            player.StorySweeps.Clear();
        }
        player.StorySweeps[stageId] = player.StorySweeps.GetValueOrDefault(stageId) + count;
    }

    private LocalRankCombatReply StoryProgress(LocalPlayer player)
    {
        var reply = new LocalRankCombatReply { operation = LocalStoryChapter.ProgressOperation };
        ulong day = TowerDay(Now);
        foreach (var stage in LocalStoryChapter.All)
        {
            uint id = (uint)stage.Id;
            bool unlocked = StoryUnlocked(player, stage);
            reply.storyStages.Add(new LocalStoryStageProgress {
                stage = id, stars = player.StoryStars.GetValueOrDefault(id, player.StoryClears.Contains(id) ? (byte)1 : (byte)0),
                unlocked = unlocked, canSweep = unlocked && StorySweepUnlocked(player, stage),
                sweepsLeft = StorySweepsLeft(player, stage, day)
            });
        }
        return reply;
    }

    private Reply SweepStory(LocalPlayer player, GsNormalBattleSkipRequest request, LocalStoryChapter stage,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (request.dungeonFlag || request.difficulty != StoryDifficulty || request.skipCount is 0 or > 10 || request.useCash)
            return fail("扫荡参数无效", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (player.ActiveStage != null || player.ActiveBattleStamp != 0)
            return fail("请先结束当前战斗", ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        if (!StoryUnlocked(player, stage) || !StorySweepUnlocked(player, stage))
            return fail(stage.SweepAfterStage == stage.Id ? "手动击败该魔王后开放扫荡" : "先手动击败该魔王并通关本篇，才能扫荡",
                ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE);
        ulong day = TowerDay(Now);
        if (request.skipCount > StorySweepsLeft(player, stage, day))
            return fail(stage.ShareRepeatRewardLimit ? "今日该魔王的碎片领取次数不足" : "今日该魔王的扫荡次数不足",
                ServerErrorCode.ERROR_NOT_ENOUGH_BATTLE_COUNT);

        var template = catalog.Stages[(uint)stage.TemplateId];
        uint cost = (uint)Math.Max(0, template.ConsumeStamina) * request.skipCount;
        if (cost > player.Stamina)
            return fail("体力不足", ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
        var perSweep = new ShopGrant { Gold = (uint)Math.Max(0, template.Gold) };
        var total = new ShopGrant { Gold = perSweep.Gold * request.skipCount };
        if (!PlanStoryReward(stage.ReplayReward, perSweep))
            return fail("剧情扫荡奖励配置无效", ServerErrorCode.ERROR_OVER_MAX_COUNT);
        foreach (var (id, quantity) in perSweep.Items)
        {
            long amount = (long)quantity * request.skipCount;
            if (amount > int.MaxValue)
                return fail("碎片数量超出库存上限", ServerErrorCode.ERROR_OVER_MAX_COUNT);
            total.Items.Add(id, (int)amount);
        }
        if (!CanPayAndGrant(player, total, BscTable.MONEY_TYPE.MONEY_TYPE_GOLD, 0))
            return fail("奖励超过库存上限", ServerErrorCode.ERROR_OVER_MAX_COUNT);

        var result = new GsNormalBattleSkipOk();
        for (uint n = 1; n <= request.skipCount; n++)
        {
            var reward = new PKBattleSkipReward { skipSequence = n, earnedCoinInfo = GrantCoins(perSweep) };
            ApplyShopGrant(player, perSweep, BscTable.MONEY_TYPE.MONEY_TYPE_GOLD, 0, reward.earnedItemList, reward.earnedCardList);
            result.earnedList.Add(reward);
        }
        UseStoryRepeatRewards(player, (uint)stage.Id, day, request.skipCount);
        if (player.Stamina >= catalog.StaminaMax(player.Level)) player.StaminaStamp = Now;
        player.Stamina -= cost;
        result.exp = player.Exp;
        result.level = player.Level;
        result.coinInfo = Coins(player);
        result.stamina = player.Stamina;
        result.staminaMax = catalog.StaminaMax(player.Level);
        result.staminaStamp = player.StaminaStamp;
        RecordObjective(player, BscTable.CHECK_TYPE.MCT_CLEAR_PVE, request.skipCount);
        return ok(result);
    }

    private Reply StartStory(LocalPlayer player, GsNormalBattleStartRequest request, LocalStoryChapter chapter,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (request.dungeonFlag != 0 || request.difficulty != StoryDifficulty)
            return fail("story chapters use normal battle mode", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (player.Level < chapter.Level)
            return fail("story chapter level requirement", ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if (chapter.PreviousStage != 0 && !player.StoryClears.Contains((uint)chapter.PreviousStage))
            return fail("先完成「" + LocalStoryChapter.Find(chapter.PreviousStage).Title + "」", ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE);
        if (chapter.RequiredHero != 0 && !HasStoryHero(player, chapter.RequiredHero))
            return fail("先解锁" + chapter.RequiredHeroName + "才能进入这条支线", ServerErrorCode.ERROR_INVALID_CARD);
        if (player.ActiveStage != null || player.Competitive.ActiveMode != 0)
            return fail("finish the active battle before entering the story", ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        var deck = DeckSlots(player, request.deckid == 0 ? 1u : request.deckid).Where(sequence => sequence != 0).ToArray();
        if (deck.Length is < 1 or > 5 || deck.Distinct().Count() != deck.Length || deck.Any(sequence => !Owns(player, sequence)))
            return fail("story requires a valid owned deck", ServerErrorCode.ERROR_INVALID_CARD);
        if (deck.Any(sequence => InTraining(player, sequence)))
            return fail("remove training cards from the story deck", ServerErrorCode.ERROR_CARD_IN_TRAINING);
        var source = catalog.Stages[(uint)chapter.TemplateId];
        var waves = catalog.StoryWaves[chapter.Id];
        uint cost = (uint)Math.Max(0, source.ConsumeStamina);
        if (player.Stamina < cost) return fail("not enough stamina for story chapter", ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);

        if (player.Stamina >= catalog.StaminaMax(player.Level)) player.StaminaStamp = Now;
        player.Stamina -= cost;
        ulong stamp = NextLocalBattleStamp(player);
        LocalBattleAttempts.Bind(player, (uint)chapter.Id, stamp);
        var strength = deck.Select(sequence =>
        {
            player.CardStates.TryGetValue(sequence, out var state);
            if (chapter.EnemyLevelReference == 0) return (int)(state?.Level ?? 1u);
            var card = catalog.RequireCard(player.CardIds[(int)sequence - 1]);
            return (int)(state?.Level ?? 1u) + (card.Rank - 4) * 2 + (int)(state?.Enchant ?? 0u);
        });
        var response = new LocalStoryBattleStartOk {
            stamina = player.Stamina, staminaStamp = player.StaminaStamp, timeStamp = stamp,
            storyEnemyLevelDelta = chapter.EnemyLevelDelta((int)strength.Average())
        };
        response.waveProbIdList.AddRange(waves);
        store.Save();
        return ok(response);
    }

    private Reply SettleStory(LocalPlayer player, GsNormalBattleEndConfirmRequest request, LocalStoryChapter chapter,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (!LocalBattleAttempts.Matches(player, request.stageId, request.timeStamp) || request.difficulty != StoryDifficulty ||
            request.dungeonFlag != 0 || player.ActiveDungeon || player.ActiveDifficulty != StoryDifficulty ||
            !Enum.IsDefined(request.endType))
            return fail("no matching story battle to settle", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        bool won = request.endType == NormalBattleEndType.NORMAL_BATTLE_SUCCESS;
        uint encodedZero = (uint)(request.timeStamp % 1000) * request.stageId;
        if (won && (request.enemyAliveCount != encodedZero ||
            request.battleHistory?.gameMode != BscTable.GAME_MODE.MODE_NORMAL ||
            request.battleEndSyncData == null || request.startCardCount == 0))
            return fail("story victory result is incomplete", ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);

        byte stars = won ? (byte)(3 - Math.Min(2u, request.cardDieCount)) : (byte)0;
        byte previousStars = player.StoryStars.GetValueOrDefault((uint)chapter.Id);
        var grant = new ShopGrant();
        bool replay = player.StoryClears.Contains((uint)chapter.Id);
        var reward = replay ? chapter.ReplayReward : chapter.FirstReward;
        bool alreadyRecruited = reward.Card != 0 && HasStoryHero(player, reward.Card);
        if (alreadyRecruited && reward.Items.Length == 0)
            reward = chapter.ShareRepeatRewardLimit ? null : chapter.ReplayReward;
        ulong day = TowerDay(Now);
        bool usesRepeatQuota = replay && chapter.ShareRepeatRewardLimit;
        bool claimReward = won && reward != null &&
            (!usesRepeatQuota || StorySweepUnlocked(player, chapter) && StorySweepsLeft(player, chapter, day) > 0) &&
            (string.IsNullOrEmpty(reward.ClaimKey) || !player.Activities.PermanentClaims.Contains(reward.ClaimKey));
        if (won)
        {
            grant.Gold = (uint)Math.Max(0, catalog.Stages[(uint)chapter.TemplateId].Gold);
            if (claimReward && reward != null && !PlanStoryReward(reward, grant,
                    ClaimedLegacyHongForge(player, chapter), alreadyRecruited) ||
                stars == 3 && previousStars < 3 && !PlanStoryReward(chapter.ThreeStarReward, grant))
                return fail("story reward exceeds storage capacity", ServerErrorCode.ERROR_OVER_MAX_COUNT);
        }
        if (!CanPayAndGrant(player, grant, BscTable.MONEY_TYPE.MONEY_TYPE_GOLD, 0))
            return fail("story reward storage is full", ServerErrorCode.ERROR_OVER_MAX_COUNT);

        var result = new GsNormalBattleEndConfirmOk { earnedCoinInfo = GrantCoins(grant) };
        ApplyShopGrant(player, grant, BscTable.MONEY_TYPE.MONEY_TYPE_GOLD, 0, result.earnedItemList, result.earnedCardList);
        if (won)
        {
            if (claimReward && usesRepeatQuota) UseStoryRepeatRewards(player, (uint)chapter.Id, day, 1);
            player.StoryClears.Add((uint)chapter.Id);
            player.StoryStars[(uint)chapter.Id] = Math.Max(previousStars, stars);
            RecordObjective(player, BscTable.CHECK_TYPE.MCT_CLEAR_PVE);
        }
        if (claimReward && reward != null && !string.IsNullOrEmpty(reward.ClaimKey)) player.Activities.PermanentClaims.Add(reward.ClaimKey);
        LocalBattleAttempts.Clear(player);
        result.exp = player.Exp;
        result.level = player.Level;
        result.clearStageID = StageUnlock.ClearStageOf(player, StoryDifficulty);
        result.coinInfo = Coins(player);
        result.stamina = player.Stamina;
        result.staminaMax = catalog.StaminaMax(player.Level);
        result.staminaStamp = player.StaminaStamp;
        return ok(result);
    }

    private static bool ClaimedLegacyHongForge(LocalPlayer player, LocalStoryChapter chapter) =>
        chapter.HongForgeReward && player.StoryClears.Contains(LocalStoryChapter.LegacyHongForgeStage);

    private bool PlanStoryReward(LocalStoryChapter.Reward reward, ShopGrant grant, bool skipBlanks = false, bool skipCard = false)
    {
        bool Add(int id, int quantity)
        {
            uint item = (uint)id;
            long total = (long)grant.Items.GetValueOrDefault(item) + quantity;
            if (quantity <= 0 || total > int.MaxValue) return false;
            grant.Items[item] = (int)total;
            return true;
        }
        if (reward.Card != 0 && !skipCard) grant.Cards.Add((uint)reward.Card);
        if (!reward.Items.All(item => Add(item.Id, item.Count))) return false;
        if (skipBlanks) return true;
        foreach (int id in reward.EquipmentBlanks)
        {
            if (!Add(id, 1)) return false;
            var blank = catalog.ItemsById[id];
            for (int i = 0; i < blank.Material1Id.Count; i++)
                if (!Add(blank.Material1Id[i], blank.Material1Value[i])) return false;
        }
        return true;
    }
}
