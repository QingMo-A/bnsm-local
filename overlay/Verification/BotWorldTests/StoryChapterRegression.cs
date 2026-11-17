using System.Text.Json;

internal static class StoryChapterRegression
{
    internal static async Task Fixture(string root, string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!folder.StartsWith(@"I:\BnsmModernWork\Verification\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Isolated verification directory required");
        Directory.CreateDirectory(folder);
        bool chapter53Preview = LocalAdventureChapter53.Enabled &&
            Path.GetFileName(folder).StartsWith("chapter53-preview-", StringComparison.Ordinal);
        bool maknaeActPreview = LocalStoryChapter.MaknaeActOnePreview &&
            Path.GetFileName(folder).StartsWith("maknae-act-preview-", StringComparison.Ordinal);
        bool hongPreview = LocalStoryChapter.HongPreview &&
            Path.GetFileName(folder).StartsWith("hong-preview-", StringComparison.Ordinal);
        await using var runtime = new LocalRuntime(root, Path.Combine(folder, "saves"), 0, 0, 0, false);
        bool savedPreview = (hongPreview || maknaeActPreview) && runtime.Store.All().Count != 0;
        if (!savedPreview) InitializeFixture(runtime, folder, chapter53Preview || hongPreview, maknaeActPreview);
        File.WriteAllText(Path.Combine(folder, "endpoints.json"), JsonSerializer.Serialize(new { directoryPort = runtime.DirectoryPort }));
        await Task.Delay(chapter53Preview || maknaeActPreview || hongPreview ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(30));
    }

    private static void InitializeFixture(LocalRuntime runtime, string folder, bool chapter53Preview, bool maknaeActPreview)
    {
        CompetitiveRegression.Prepare(runtime);
        var player = runtime.Store.All().Single();
        if (!player.CardIds.Contains(30168u)) player.CardIds.Add(30168);
        if (chapter53Preview || maknaeActPreview)
        {
            player.ClearStage = LocalAdventureChapter53.PreviousStage;
            player.ClearEpisodeCutScene = (player.ClearStage << 8) + 1;
        }
        if (Path.GetFileName(folder).StartsWith("hong-growth-native-", StringComparison.Ordinal))
        {
            player.Level = 99;
            player.Gold = 1000000000;
            foreach (int id in new[] { LocalHongCard.FirstCardId + 4, LocalHongCard.FirstCardId + 7, LocalHongCard.LastCardId })
            {
                player.CardIds.Add((uint)id);
                player.CardStates[(ulong)player.CardIds.Count] = new LocalCardState { Level = 61, Enchant = 6 };
            }
            player.Inventory[LocalHongCard.FragmentId] = 10000;
            player.Inventory[290000001] = 10000;
        }
        else if (maknaeActPreview || Path.GetFileName(folder).StartsWith("maknae-native-", StringComparison.Ordinal))
        {
            var choices = runtime.Catalog.UsableHeroes.Where(row => row.Rank == 8 &&
                row.Talent <= BscTable.CARD_TALENT.CARD_TALENT_S).ToArray();
            bool alternate = maknaeActPreview && Environment.GetEnvironmentVariable("BNSM_MAKNAE_TEST_TEAM") == "sustain";
            int[] roles = alternate ? [3, 2, 6, 7, 5] : [2, 3, 4, 5, 6];
            int previewLevel = int.TryParse(Environment.GetEnvironmentVariable("BNSM_MAKNAE_TEST_LEVEL"), out int levelOption)
                ? Math.Clamp(levelOption, 25, 99) : 55;
            for (int index = 0; index < player.Deck.Count && index < roles.Length; index++)
            {
                ulong sequence = player.Deck[index];
                if (sequence == 0) continue;
                var replacement = choices.Where(row => (int)row.Class == roles[index])
                    .OrderByDescending(row => row.Talent).ThenBy(row => row.Id).First();
                player.CardIds[(int)sequence - 1] = (uint)replacement.Id;
                if (maknaeActPreview)
                {
                    uint level = (uint)Math.Min(previewLevel, ProgressionRules.CardMax(runtime.Catalog, player, replacement));
                    player.CardStates[sequence] = new LocalCardState {
                        Level = level, Exp = ProgressionRules.CardFloor(runtime.Catalog, level),
                        ActiveSkills = [level, level], PassiveSkills = [level, level, level, level, level]
                    };
                }
            }
            for (int index = 0; index < player.CardIds.Count; index++)
                if (runtime.Catalog.Cards.Any(row => row.Id == player.CardIds[index] &&
                    LocalStoryChapter.IsMaknaeFamily(row.EvolveFirstAncestor))) player.CardIds[index] = 30081;
            if (!maknaeActPreview) player.StoryClears.UnionWith(new uint[] { 8611, 8612, 8614, 8615, 8617, 8618 });
            else player.Inventory.Remove(LocalStoryChapter.MaknaeFragmentId);
        }
        runtime.Store.Save();
    }

    internal static async Task Run(string root)
    {
        string save = Path.Combine(root, "..", "Verification", "story-protocol-" + Guid.NewGuid().ToString("N"), "saves");
        await using var runtime = new LocalRuntime(root, save, 0, 0, 0, false);
        FactionRegression.Prepare(runtime);
        var player = runtime.Store.All().Single();
        var catalog = runtime.Catalog;
        using var peer = new Peer(root);
        await peer.Login(runtime, player.Account);
        var checks = new List<string>();
        void Check(bool pass, string note)
        {
            if (!pass) throw new InvalidDataException(note);
            checks.Add(note);
            Console.WriteLine("PASS " + note);
        }

        const GameServerCommand Start = GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST;
        const GameServerCommand End = GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST;
        GsNormalBattleStartRequest Enter(LocalStoryChapter chapter) => new() { stageid = (uint)chapter.Id, deckid = 1 };
        GsNormalBattleEndConfirmRequest Result(LocalStoryChapter chapter, ulong stamp, bool win) => new() {
            stageId = (uint)chapter.Id, timeStamp = stamp,
            endType = win ? NormalBattleEndType.NORMAL_BATTLE_SUCCESS : NormalBattleEndType.NORMAL_BATTLE_FAIL,
            enemyAliveCount = (uint)(stamp % 1000) * (uint)chapter.Id,
            battleHistory = new() { gameMode = BscTable.GAME_MODE.MODE_NORMAL },
            battleEndSyncData = new() { clearTime = 23 }, startCardCount = 5
        };
        async Task<GsNormalBattleEndConfirmOk> Win(LocalStoryChapter chapter)
        {
            var started = await peer.Request<GsNormalBattleStartOk>(Start, Enter(chapter));
            return await peer.Request<GsNormalBattleEndConfirmOk>(End, Result(chapter, started.timeStamp, true));
        }
        if (LocalStoryChapter.HongPreview)
        {
            const GameServerCommand sweepCommand = GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST;
            var route = LocalStoryChapter.All.Where(stage => stage.StoryId is "hong" or "hong-forge")
                .OrderBy(stage => stage.Id).ToArray();
            var bosses = route.Where(stage => stage.Boss).ToArray();
            var forgeStart = LocalStoryChapter.HongForge;
            var routeBlanks = new List<PKUserItem>();
            player.ClearStage = LocalAdventureChapter53.PreviousStage;
            player.ClearEpisodeCutScene = (player.ClearStage << 8) + 1;
            uint originalClear = player.ClearStage;
            player.Stamina = 9999;
            await peer.Request<FailPacket>(Start, Enter(forgeStart), success: false);
            await peer.Request<FailPacket>(Start, Enter(route[2]), success: false);
            Check(route.Length == 18 && bosses.Length == 5 && route.Select(stage => stage.TemplateId).Distinct().Count() >= 15,
                "Hong routes contain eighteen encounters and five sweepable bosses");
            foreach (var stage in route)
            {
                var started = await peer.Request<LocalStoryBattleStartOk>(Start, Enter(stage));
                var victory = await peer.Request<GsNormalBattleEndConfirmOk>(End, Result(stage, started.timeStamp, true));
                bool recruited = victory.earnedCardList.Any(row => row.cardID == LocalHongCard.FirstCardId);
                Check(started.waveProbIdList.SequenceEqual(catalog.StoryWaves[stage.Id]) &&
                    recruited == (stage.Id == 8648), "Fixed waves and correct first-clear recruitment at " + stage.Id);
                foreach (var item in victory.earnedItemList.Where(item => LocalHongEquipment.IsBlank((int)item.itemID)))
                {
                    var recipe = catalog.ItemsById[(int)item.itemID];
                    var craftedBlank = await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,
                        new GsEquipmentCraftRequest { baseEquipmentSeq = item.itemSeq, targetEquipmentId = (uint)recipe.Next1Id });
                    Check(craftedBlank.result == CraftResult.CRAFT_RESULT_SUCCESS, "First-clear materials forge " + recipe.Id);
                    routeBlanks.Add(item);
                }
                if (stage.Id == 8644)
                {
                    var sweep = new GsNormalBattleSkipRequest { stageId = (uint)stage.Id, skipCount = 1 };
                    await peer.Request<FailPacket>(sweepCommand, sweep, success: false);
                    var earlyRepeat = await Win(stage);
                    Check(earlyRepeat.earnedItemList.Count == 0, "Repeat materials stay locked until the route finale");
                }
            }
            foreach (var boss in bosses)
            {
                var sweep = new GsNormalBattleSkipRequest { stageId = (uint)boss.Id, skipCount = 2 };
                var before = boss.ReplayReward.Items.ToDictionary(item => item.Id,
                    item => player.Inventory.GetValueOrDefault((uint)item.Id));
                await Win(boss);
                await peer.Request<GsNormalBattleSkipOk>(sweepCommand, sweep);
                await peer.Request<FailPacket>(sweepCommand,
                    new GsNormalBattleSkipRequest { stageId = (uint)boss.Id, skipCount = 1 }, success: false);
                Check(boss.ReplayReward.Items.All(item => player.Inventory[(uint)item.Id] == before[item.Id] + item.Count * 3),
                    "Manual clear and sweep share exactly three repeat claims at " + boss.Id);
            }
            runtime.Store.Save();
            var reloaded = new PlayerStore(save, catalog).Find(player.Uid)!;
            Check(reloaded.StoryClears.Count == 18, "Reload retains all eighteen clears");
            Check(reloaded.ClearStage == originalClear, "Story progression never advances the adventure");
            Check(reloaded.CardIds.Count(card => card == LocalHongCard.FirstCardId) == 1, "Reload retains one recruit");
            Check(routeBlanks.Select(item => (int)item.itemID).Order().SequenceEqual(LocalHongEquipment.Blanks.Order()),
                "Every equipment blank comes from its assigned stage");
            Console.WriteLine("HONG_PREVIEW_PASSED " + checks.Count);
            return;
        }
        if (LocalStoryChapter.MaknaeActOnePreview)
        {
            const GameServerCommand sweepCommand = GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST;
            GsNormalBattleSkipRequest SweepRequest(int stage, uint count) => new() { stageId = (uint)stage, skipCount = count };
            var route = LocalStoryChapter.All.Where(stage => stage.StoryId == "maknae").OrderBy(stage => stage.Id).ToArray();
            var bosses = route.Where(stage => stage.Boss).ToArray();
            Check(route.GroupBy(stage => stage.Act).Select(group => group.Count()).SequenceEqual(new[] { 5, 5, 6 }) &&
                route.Count(stage => stage.BattleOnly) == 6 && bosses.Length == 3, "Maknae opens three distinct acts with sixteen battles and three bosses");
            uint originalClear = player.ClearStage;
            for (int index = 0; index < player.CardIds.Count; index++)
                if (catalog.Cards.Any(card => card.Id == player.CardIds[index] && LocalStoryChapter.IsMaknaeFamily(card.EvolveFirstAncestor)))
                    player.CardIds[index] = 30081;
            player.Inventory.Remove(LocalStoryChapter.MaknaeFragmentId);
            player.Stamina = 9999;
            await peer.Request<FailPacket>(Start, Enter(route[^1]), success: false);
            await peer.Request<FailPacket>(sweepCommand, SweepRequest(bosses[0].Id, 1), success: false);
            foreach (var stage in route)
            {
                var previewStart = await peer.Request<LocalStoryBattleStartOk>(Start, Enter(stage));
                Check(previewStart.waveProbIdList.SequenceEqual(catalog.StoryWaves[stage.Id]) &&
                    previewStart.storyEnemyLevelDelta >= -stage.EnemyLevelMaxDecrease && previewStart.storyEnemyLevelDelta <= stage.EnemyLevelMaxIncrease,
                    "Fixed waves and bounded scaling at " + stage.Id);
                var end = Result(stage, previewStart.timeStamp, true);
                var win = await peer.Request<GsNormalBattleEndConfirmOk>(End, end);
                Check(win.earnedCardList.Any(row => row.cardID == LocalStoryChapter.MaknaeCardId) == (stage.Id == route[^1].Id),
                    "Only the final battle recruits SS Maknae at " + stage.Id);
            }
            Check(player.Inventory[LocalStoryChapter.MaknaeFragmentId] == 24 && player.ClearStage == originalClear,
                "Permanent rewards total 24 fragments with three stars in every boss battle");
            foreach (var boss in bosses)
            {
                int count = boss.ReplayReward.Items.Single().Count;
                int before = player.Inventory[LocalStoryChapter.MaknaeFragmentId];
                await Win(boss);
                var sweep = SweepRequest(boss.Id, 2);
                var skip = await peer.Request<GsNormalBattleSkipOk>(sweepCommand, sweep);
                await peer.Request<GsNormalBattleSkipOk>(sweepCommand, sweep, retry: true);
                await peer.Request<FailPacket>(sweepCommand, SweepRequest(boss.Id, 1), success: false);
                var extra = await Win(boss);
                Check(skip.earnedList.Count == 2 && extra.earnedItemList.Count == 0 &&
                    player.Inventory[LocalStoryChapter.MaknaeFragmentId] == before + count * 3 && player.StorySweeps[(uint)boss.Id] == 3,
                    "Manual wins and sweeps share three reward claims at " + boss.Id);
            }
            var previewReload = new PlayerStore(save, catalog).Find(player.Uid)!;
            Check(previewReload.StoryClears.Count == 16 && previewReload.StorySweeps.Values.Sum(value => (int)value) == 9 &&
                previewReload.Inventory[LocalStoryChapter.MaknaeFragmentId] == 42 && previewReload.CardIds.Count(id => id == LocalStoryChapter.MaknaeCardId) == 1,
                "Reload preserves sixteen clears, the recruit and exact fragments");
            player.StorySweepDay = ProtocolRouter.TowerDay(ProtocolRouter.Now) - 1;
            await peer.Request<GsNormalBattleSkipOk>(sweepCommand, SweepRequest(bosses[0].Id, 1));
            Check(player.StorySweeps.Count == 1 && player.StorySweeps[(uint)bosses[0].Id] == 1 && player.Inventory[LocalStoryChapter.MaknaeFragmentId] == 43,
                "Daily reset refreshes repeat claims without repeating first-clear or star rewards");
            Console.WriteLine("MAKNAE_PREVIEW_PASSED " + checks.Count);
            return;
        }
        var card = catalog.Cards.Single(x => x.Id == LocalHongCard.FirstCardId);
        Check(card.Faction == BscTable.CARD_FACTION.FACTION_JUNGPA &&
            card.Class == BscTable.CARD_CLASS.CLASS_KUNGFU_MASTER && card.AttackType == BscTable.ATTACK_TYPE.ATTACK_FORCE &&
            card.Talent == BscTable.CARD_TALENT.CARD_TALENT_SS, "Qinglong SS force kungfu identity");
        Check(catalog.UsableHeroes.Count(x => LocalHongCard.IsCard(x.Id)) == 16 &&
            Enumerable.Range(1, 15).All(rank => catalog.Cards.Single(x => x.Id == LocalHongCard.FirstCardId + rank - 1).RankUpCardId == LocalHongCard.FirstCardId + rank),
            "Sixteen owned-card rank rows with a contiguous evolution chain");
        Check(!catalog.DrawPool(true, 4).Any(x => x.Talent >= BscTable.CARD_TALENT.CARD_TALENT_SS) &&
            catalog.DrawPool(true, 4).Any(x => x.Talent == BscTable.CARD_TALENT.CARD_TALENT_S) && GachaRules.Load(root).STalentWeight == 25,
            "SS excluded and S has a quarter of normal candidate weight");

        var entry = LocalCardRegistry.FindExtensionForCard(card.Id)!;
        Check(LocalStoryChapter.All.Select(stage => stage.Id).Order().SequenceEqual(new[] { 8601, 8602, 8603, 8604 }.Concat(Enumerable.Range(8611, 9))) &&
            catalog.StoryWaves.All(pair => pair.Value.SequenceEqual(catalog.WaveIds(BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,
                (uint)LocalStoryChapter.Find(pair.Key).TemplateId))) &&
            catalog.StoryForgeSources.Count == 4, "Shared story catalog preserves stage IDs, fixed native waves and forge sources");
        LocalStoryChapter Example(int id, int previous) => new()
        {
            Id = id, TemplateId = 49, PreviousStage = previous, Level = 25, Line = "Character",
            MenuLabel = "测试", Title = "测试关卡", Opening = "开场", Scene = "场景", Goal = "目标",
            BattleArt = "BellRoadBattle",
            EnemyLevelReference = 40, EnemyLevelMaxIncrease = 6, EnemyLevelMaxDecrease = 3
        };
        var definition = new LocalStoryChapter.Definition { Version = 1, Stages = [Example(1, 0), Example(2, 1)] };
        var configured = LocalStoryChapter.Validate(definition)[2];
        Check(configured.PreviousStage == 1 && configured.EnemyLevelDelta(99) == 6 &&
            configured.EnemyLevelDelta(1) == -3 && configured.EnemyLevelDelta(40) == 0, "New story stages can chain and bound level scaling");
        bool rejectsCycle = false;
        definition.Stages[0].PreviousStage = 2;
        try { LocalStoryChapter.Validate(definition); }
        catch (InvalidDataException) { rejectsCycle = true; }
        Check(rejectsCycle, "Cyclic story prerequisites fail validation");
        Check(entry.Chapter == LocalStoryChapter.Hong && entry.CollectionId == card.CollectionId &&
            LocalCardRegistry.FindFragment(entry.FragmentId) == entry &&
            catalog.Pieces.Single(piece => piece.ItemId == entry.FragmentId).NextId == card.Id,
            "Card, fragment recipe, collection and chapter share the registered identity");
        var bonds = LocalCardRegistry.Relations().OrderBy(row => row.Id).ToArray();
        int[][] members = [[30181, card.Id, 30081], [30121, 50171, card.Id], [30161, 20181, 70111, card.Id]];
        int[][] bondFirst = [[24, 17, 11, 11, 10], [17, 11, 14, 10, 10], [17, 20, 20, 20, 10]];
        int[][] bondLast = [[538, 478, 478, 166, 20], [478, 478, 168, 20, 20], [478, 202, 160, 188, 20]];
        Check(bonds.Length == 3 && bonds[1].RankData[0].StatValueIdType[0] == BscTable.EQEFFECT_TYPE.EQEFFECT_ADD_F_DAMAGE &&
            Enumerable.Range(0, 3).All(i => bonds[i].CardId.SequenceEqual(members[i]) &&
                bonds[i].CardId.All(id => catalog.Cards.Any(row => row.Id == id)) &&
                bonds[i].RankData.Select(row => row.Rank).SequenceEqual(Enumerable.Range(1, 16)) &&
                bonds[i].RankData[0].StatValueId.SequenceEqual(bondFirst[i]) &&
                bonds[i].RankData[15].StatValueId.SequenceEqual(bondLast[i])), "Three native bonds have the requested members and rank endpoints");

        int[] topStart = [1900044020, 1900044120, 1900044240];
        int[] topEnd = [1900044038, 1900044138, 1900044259];
        int[][] gearFirst = [[22, 14, 11, 46, 20, 0, 20], [136, 17, 11, 46, 20, 8], [34, 16, 15, 11, 20, 8, 50, 50]];
        int[][] gearLast = [[305, 226, 89, 262, 20, 950, 20], [19500, 199, 87, 245, 20, 107], [588, 256, 318, 107, 24, 107, 60, 60]];
        Check(Enumerable.Range(0, 3).All(i => catalog.ItemsById[topStart[i]].AttributeValueId.SequenceEqual(gearFirst[i]) &&
            catalog.ItemsById[topEnd[i]].AttributeValueId.SequenceEqual(gearLast[i]) &&
            catalog.ItemsById[topEnd[i]].CardId == card.Id &&
            catalog.ItemsById[topEnd[i]].Next1Id == 0), "All three exclusives bind to Hong and have the requested first and final stats");

        uint[] previewIds = [(uint)entry.CardOfferId, (uint)entry.FragmentOfferId, ..LocalHongEquipment.MarketPreviews().Select(row => (uint)row.GoodsId)];
        var offers = new FullShopCatalog(catalog);
        Check(offers.Goods[previewIds[0]].ProductId == card.Id &&
            offers.Goods[previewIds[1]].ProductId == entry.FragmentId &&
            offers.Goods.Values.Count(row => row.ProductId == entry.FragmentId) == 1 &&
            previewIds.Skip(2).All(id => LocalHongEquipment.IsItem(offers.Goods[id].ProductId)),
            "Boss shop lists the card, fragment and all three equipment acquisition routes");
        foreach (uint shop in new uint[] { 91, 92 })
        {
            var query = new GsGetIngameShopSellingListRequest();
            query.shopId.AddRange(new[] { shop, FullShopCatalog.QueryMarker });
            query.shopId.AddRange(previewIds);
            var list = await peer.Request<GsGetIngameShopSellingListOk>(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST, query);
            Check(list.shopSetDataList.Single().goodsList.Select(row => row.packageId).SequenceEqual(previewIds),
                $"Shop {shop} returns the registered display entries");
            string State() => JsonSerializer.Serialize(new { player.Gold, player.CashPoint, player.Inventory, player.CardIds });
            string before = State();
            for (int slot = 0; slot < previewIds.Length; slot++)
            {
                var buy = new LocalShopBuyRequest { shopId = shop, packageId = previewIds[slot], SlotIndex = (uint)slot };
                var failure = await peer.Request<FailPacket>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST, buy, success: false);
                Check(failure.errorCode == (uint)ServerErrorCode.ERROR_INVALID_BUY_PRODUCT_INFO && State() == before,
                    $"Shop {shop} cannot purchase story entry {slot}");
            }
        }

        player.Stamina = 1000;
        player.Level = 24;
        await peer.Request<FailPacket>(Start, Enter(LocalStoryChapter.Hong), success: false);
        Check(player.ActiveStage == null && player.Stamina == 1000, "Level gate has no admission side effects");
        player.Level = 99;
        player.Exp = ProgressionRules.PlayerFloor(catalog, player.Level);
        await peer.Request<FailPacket>(Start, Enter(LocalStoryChapter.HongForge), success: false);
        Check(player.ActiveStage == null && player.Stamina == 1000, "Hong forge chapter requires ownership before spending stamina");
        uint clear = player.ClearStage;
        var chapterToScale = LocalStoryChapter.Hong;
        chapterToScale.EnemyLevelReference = 1;
        chapterToScale.EnemyLevelMaxIncrease = 3;
        LocalStoryBattleStartOk scaled;
        try { scaled = await peer.Request<LocalStoryBattleStartOk>(Start, Enter(chapterToScale)); }
        finally { chapterToScale.EnemyLevelReference = chapterToScale.EnemyLevelMaxIncrease = 0; }
        var admitted = (GsNormalBattleStartOk)WireCodec.Deserialize(typeof(GsNormalBattleStartOk), peer.LastPayload)!;
        Check(admitted.waveProbIdList.Count == 3 && admitted.timeStamp != 0, "Custom chapter uses three native enemy waves");
        Check(scaled.storyEnemyLevelDelta == 3 && scaled.timeStamp == admitted.timeStamp &&
            scaled.waveProbIdList.SequenceEqual(admitted.waveProbIdList), "Story scaling adds a frozen level delta without changing the native wave contract");
        await peer.Request<FailPacket>(End, Result(LocalStoryChapter.Hong, admitted.timeStamp + 1, true), success: false);
        await peer.Request<GsNormalBattleEndConfirmOk>(End, Result(LocalStoryChapter.Hong, admitted.timeStamp, false));
        Check(player.ActiveStage == null && !player.StoryClears.Contains((uint)LocalStoryChapter.Hong.Id) &&
            !player.CardIds.Contains((uint)LocalHongCard.FirstCardId), "Retreat and stale settlement do not unlock the card");
        admitted = await peer.Request<GsNormalBattleStartOk>(Start, Enter(LocalStoryChapter.Hong));
        var claim = Result(LocalStoryChapter.Hong, admitted.timeStamp, true);
        var clearObjective = BscTable.CHECK_TYPE.MCT_CLEAR_PVE;
        uint clearsBefore = player.Missions.Counters.GetValueOrDefault(clearObjective);
        var unlocked = await peer.Request<GsNormalBattleEndConfirmOk>(End, claim);
        var receipt = peer.LastPayload.ToArray();
        await peer.Request<GsNormalBattleEndConfirmOk>(End, claim, retry: true);
        uint receiptSequence = claim.keyInfo.sequenceNo;
        Check(unlocked.earnedCardList.Single().cardID == LocalHongCard.FirstCardId &&
            player.CardIds.Count(id => id == LocalHongCard.FirstCardId) == 1 && receipt.SequenceEqual(peer.LastPayload) &&
            player.ClearStage == clear, "First victory unlocks once and leaves main story clear intact");
        Check(player.Missions.Counters.GetValueOrDefault(clearObjective) == clearsBefore + 1,
            "Story settlement emits the objective once across an identical retry");
        var replayStarted = await peer.Request<GsNormalBattleStartOk>(Start, Enter(LocalStoryChapter.Hong));
        uint currentSequence = claim.keyInfo.sequenceNo;
        claim.keyInfo.sequenceNo = receiptSequence;
        try { await peer.Request<FailPacket>(End, claim, retry: true, success: false); }
        finally { claim.keyInfo.sequenceNo = currentSequence; }
        Check(player.ActiveBattleStamp == replayStarted.timeStamp, "An old success receipt cannot close a newer story battle");
        await peer.Request<GsNormalBattleEndConfirmOk>(End, Result(LocalStoryChapter.Hong, replayStarted.timeStamp, false));
        var book = await peer.Request<GsGetCardCollectionInfoOk>(GameServerCommand.GS_GET_CARD_COLLECTION_INFO_REQUEST,
            new GsGetCardCollectionInfoRequest { targetUid = player.Uid });
        int collectionBit = entry.CollectionId - 1;
        Check((book.currentCardCollectInfo[collectionBit / 64] & (1UL << (collectionBit % 64))) != 0,
            "Story unlock sets the native collection bit");
        var repeat = await Win(LocalStoryChapter.Hong);
        Check(repeat.earnedCardList.Count == 0 && repeat.earnedItemList.Single().itemID == LocalHongCard.FragmentId &&
            player.Inventory[(uint)LocalHongCard.FragmentId] == 10, "Chapter replay grants only the new hero's growth fragments and gold");

        var forgeStage = LocalStoryChapter.HongForge;
        forgeStage.PreviousStage = LocalStoryChapter.Weapon.Id;
        try
        {
            uint staminaAtGate = player.Stamina;
            await peer.Request<FailPacket>(Start, Enter(forgeStage), success: false);
            Check(player.ActiveStage == null && player.Stamina == staminaAtGate,
                "Configured prerequisite rejects admission before spending stamina");
        }
        finally { forgeStage.PreviousStage = 0; }

        var activeStory = await peer.Request<GsNormalBattleStartOk>(Start, Enter(LocalStoryChapter.Hong));
        player.Board.ActiveStamp = activeStory.timeStamp - 1;
        player.Board.Node = (uint)LocalStoryChapter.Hong.Id;
        player.Resources.ActiveStamp = activeStory.timeStamp - 2;
        player.ChallengeTower.ActiveStamp = activeStory.timeStamp - 3;
        uint staminaAfterStart = player.Stamina;
        using (var reconnected = new Peer(root))
        {
            await reconnected.Login(runtime, player.Account);
            Check(player.ActiveStage == null && player.ActiveBattleStamp == 0 && player.Board.ActiveStamp == 0 &&
                player.Resources.ActiveStamp == 0 && player.ChallengeTower.ActiveStamp == 0 && player.Stamina == staminaAfterStart,
                "Reconnect clears all abandoned attempts without a second stamina charge");
            var restarted = await reconnected.Request<GsNormalBattleStartOk>(Start, Enter(LocalStoryChapter.Hong));
            await peer.Request<FailPacket>(End, Result(LocalStoryChapter.Hong, activeStory.timeStamp, true), success: false);
            Check(restarted.timeStamp > activeStory.timeStamp && player.ActiveBattleStamp == restarted.timeStamp,
                "A result from before reconnect cannot settle the new battle");
            await reconnected.Request<GsNormalBattleEndConfirmOk>(End, Result(LocalStoryChapter.Hong, restarted.timeStamp, false));
        }

        var forge = await Win(LocalStoryChapter.HongForge);
        var blanks = forge.earnedItemList.Where(row => LocalHongEquipment.IsBlank((int)row.itemID)).ToArray();
        Check(blanks.Select(row => (int)row.itemID).Order().SequenceEqual(LocalHongEquipment.Blanks) &&
            blanks.All(row => row.itemSeq != 0) && (await Win(LocalStoryChapter.HongForge)).earnedItemList.Count == 0,
            "Hong forge delivers the three unique blanks on first clear only");
        foreach (var item in blanks)
        {
            var source = catalog.ItemsById[(int)item.itemID];
            var result = await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,
                new GsEquipmentCraftRequest { baseEquipmentSeq = item.itemSeq, targetEquipmentId = (uint)source.Next1Id });
            Check(result.result == CraftResult.CRAFT_RESULT_SUCCESS &&
                player.EquipmentItems.Single(row => row.Sequence == item.itemSeq).ItemId == source.Next1Id,
                "Original forge uses the chapter's actual materials for " + source.NameAlias);
        }
        var gauntlet = player.EquipmentItems.Single(row => row.ItemId == 1900044200);
        player.Gold = 1000000000;
        while (gauntlet.ItemId != 1900044259)
        {
            var source = catalog.ItemsById[(int)gauntlet.ItemId];
            bool awakeGear = source.Next1Id == 0;
            uint next = (uint)(awakeGear ? source.AwakningNextId : source.Next1Id);
            if (next == 0) throw new InvalidDataException("Hong gauntlet upgrade chain stops before +19");
            var materialIds = awakeGear ? source.AwakningMaterialId : source.Material1Id;
            var quantities = awakeGear ? source.AwakningMaterialValue : source.Material1Value;
            for (int i = 0; i < materialIds.Count; i++) player.Inventory[(uint)materialIds[i]] = quantities[i];
            if (awakeGear)
                await peer.Request<GsEquipmentAwakeOk>(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,
                    new GsEquipmentAwakeRequest { baseEquipmentSeq = gauntlet.Sequence, targetEquipmentId = next });
            else
                await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,
                    new GsEquipmentCraftRequest { baseEquipmentSeq = gauntlet.Sequence, targetEquipmentId = next });
            if (gauntlet.ItemId != next) throw new InvalidDataException("Hong gauntlet was not upgraded to the requested step");
        }
        Check(player.EquipmentItems.Count(row => row.Sequence == gauntlet.Sequence) == 1, "Native forge and awakening reach Super +19 with one gauntlet instance");

        player.CardIds.RemoveAll(id => catalog.Cards.Any(c => c.Id == id && c.EvolveFirstAncestor == LocalStoryChapter.WeaponOwnerId));
        uint stamina = player.Stamina;
        await peer.Request<FailPacket>(Start, Enter(LocalStoryChapter.Weapon), success: false);
        Check(player.Stamina == stamina && player.ActiveStage == null, "Weapon side story requires ownership before spending stamina");
        player.CardIds.Add(30168);
        var blank = await Win(LocalStoryChapter.Weapon);
        var weapon = blank.earnedItemList.Single(i => i.itemID == LocalStoryChapter.WeaponBlankId);
        Check(weapon.itemSeq != 0 && blank.earnedItemList.Count == 3 &&
            player.EquipmentItems.All(x => x.ItemId != LocalStoryChapter.WeaponId), "Side story delivers a unique blank and its two original crafting ingredients");
        var crafted = await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST, new GsEquipmentCraftRequest {
            baseEquipmentSeq = weapon.itemSeq, targetEquipmentId = LocalStoryChapter.WeaponId
        });
        Check(crafted.result == CraftResult.CRAFT_RESULT_SUCCESS &&
            player.EquipmentItems.Single(i => i.Sequence == weapon.itemSeq).ItemId == LocalStoryChapter.WeaponId,
            "Native forge transforms the blank into the original exclusive weapon");

        var outfit = await Win(LocalStoryChapter.Outfit);
        int clothing = player.Inventory.GetValueOrDefault((uint)LocalHelperCostumeDefinition.FirstItemId);
        var again = await Win(LocalStoryChapter.Outfit);
        Check(outfit.earnedItemList.Any(i => i.itemID == LocalHelperCostumeDefinition.FirstItemId) &&
            again.earnedItemList.Count == 0 && clothing == player.Inventory.GetValueOrDefault((uint)LocalHelperCostumeDefinition.FirstItemId) &&
            player.Activities.PermanentClaims.Contains(LocalStoryChapter.NightSakuraFirstClaim),
            "Night Sakura first-clear and activity stage share the same one-time reward");
        var restored = new PlayerStore(save, catalog).Find(player.Uid)!;
        Check(restored.StoryClears.Count == 4 && restored.CardIds.Contains((uint)LocalHongCard.FirstCardId) &&
            restored.EquipmentItems.Any(i => i.Sequence == weapon.itemSeq && i.ItemId == LocalStoryChapter.WeaponId) &&
            restored.EquipmentItems.Any(i => i.Sequence == gauntlet.Sequence && i.ItemId == 1900044259) && restored.ActiveStage == null,
            "Story, owned card and crafted exclusive weapons survive storage reload");
        ulong hongSeq = (ulong)player.CardIds.IndexOf((uint)LocalHongCard.FirstCardId) + 1;
        player.CardIds[(int)hongSeq - 1] = LocalHongCard.LastCardId;
        player.Level = 99;
        player.Gold = 1000000000;
        var hongState = new LocalCardState { Level = 99, Enchant = 5 };
        player.CardStates[hongSeq] = hongState;
        const uint formerEnchantFragment = 205081309;
        player.Inventory[formerEnchantFragment] = 10000;
        player.Inventory[LocalHongCard.FragmentId] = 0;
        player.Inventory[290000001] = 10000;
        var wrongMaterial = new GsSkillEnchantRequest { cardSeq = hongSeq, skillType = (CardSkillType)0 };
        var denied = await peer.Request<FailPacket>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST, wrongMaterial, success: false);
        Check(denied.errorCode == (uint)ServerErrorCode.ERROR_NOT_ENOUGH_MATERIAL &&
            hongState.SkillEnchants.All(level => level == 0) && player.Inventory[formerEnchantFragment] == 10000,
            "A different hero's fragments cannot upgrade Hong");
        player.Inventory[LocalHongCard.FragmentId] = 10000;
        foreach (int slot in Enumerable.Range(0, LocalHongCard.EnchantSlots))
        {
            var rules = catalog.SkillEnchants[LocalHongCard.EnchantGroupId + slot].SkillEnchantInfoList;
            foreach (var rule in rules)
            {
                int index = rule.MaterialItemId.IndexOf(LocalHongCard.FragmentId);
                int held = player.Inventory[LocalHongCard.FragmentId];
                var request = new GsSkillEnchantRequest { cardSeq = hongSeq, skillType = (CardSkillType)slot };
                var response = await peer.Request<GsSkillEnchantOk>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST, request);
                if (index < 0 || response.result != CraftResult.CRAFT_RESULT_SUCCESS ||
                    response.cardInfo.cardSkillEnchant[slot] != rule.EnchantLevel ||
                    player.Inventory[LocalHongCard.FragmentId] != held - rule.MaterialItemQuantity[index])
                    throw new InvalidDataException($"Hong skill enchant failed at slot {slot}, tier {rule.EnchantLevel}");
            }
            Check(rules.Count == 5 && hongState.SkillEnchants[slot] == 5, $"Hong skill slot {slot} enchants to +5 with its own fragments");
        }
        var sourceTrans = catalog.Transcendence.Single(x => x.CardId == LocalHongCard.TranscendenceTemplateId);
        var hongTrans = catalog.Transcendence.Single(x => x.CardId == LocalHongCard.LastCardId);
        Check(hongTrans.GrowthItemId == LocalHongCard.FragmentId && hongTrans.GrowthCancelItemId == LocalHongCard.FragmentId &&
            hongTrans.GrowthItemValue == sourceTrans.GrowthItemValue && hongTrans.GrowthCancelItemValue == sourceTrans.GrowthCancelItemValue &&
            hongTrans.EffectDataList.Select(x => x.Id).SequenceEqual(sourceTrans.EffectDataList.Select(x => x.Id)) &&
            hongTrans.EffectDataList.All(effect => effect.EffectGradeDataList.Skip(1).All(grade => grade.GradeItemId == LocalHongCard.FragmentId)),
            "Hong inherits the original transcendence structure and all material slots use his fragments");
        await peer.Request<GsGetTranscendenceInfoOk>(GameServerCommand.GS_GET_TRANSCENDENCE_INFO_REQUEST,
            new GsGetTranscendenceInfoRequest { cardSeq = hongSeq });
        player.Inventory[LocalHongCard.FragmentId] = hongTrans.GrowthItemValue + hongTrans.GrowthCancelItemValue;
        var growth = await peer.Request<GsApplyTranscendenceGrowthOk>(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,
            new GsApplyTranscendenceGrowthRequest { cardSeq = hongSeq });
        Check(growth.cardInfo.effectDataInfoList.Count == 3 && player.Inventory[LocalHongCard.FragmentId] == hongTrans.GrowthCancelItemValue,
            "Hong's transcendence packet consumes his fragments and opens three original effects");
        await peer.Request<GsCancelTranscendenceGrowthOk>(GameServerCommand.GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST,
            new GsCancelTranscendenceGrowthRequest { cardSeq = hongSeq });
        Check(hongState.TranscendenceEffects.Count == 0 && player.Inventory[LocalHongCard.FragmentId] == hongTrans.GrowthItemValue,
            "Hong's transcendence undo refunds growth material and charges his own cancellation cost");
        var previousEffect = hongTrans.EffectDataList[0];
        hongState.TranscendenceEffects.Add(new() { Id = (uint)previousEffect.Id, Grade = 1, Value = (uint)previousEffect.EffectGradeDataList[0].EffectMax });
        int breakthroughCost = previousEffect.EffectGradeDataList[1].GradeValue;
        player.Inventory[LocalHongCard.FragmentId] = breakthroughCost;
        var breakthrough = await peer.Request<GsBreakThroughTranscendenceGrowthOk>(GameServerCommand.GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST,
            new GsBreakThroughTranscendenceGrowthRequest { cardSeq = hongSeq, effectDataId = (uint)previousEffect.Id });
        Check(breakthrough.cardInfo.effectDataInfoList.Single().effectGrade == 2 && player.Inventory[LocalHongCard.FragmentId] == 0,
            "Hong's native breakthrough uses his own fragments");
        var grown = new PlayerStore(save, catalog).Find(player.Uid)!.CardStates[hongSeq];
        Check(grown.SkillEnchants.Take(LocalHongCard.EnchantSlots).All(level => level == 5) && grown.TranscendenceEffects.Single().Grade == 2,
            "Hong skill enchant and transcendence growth survive storage reload");
        var maknae = LocalStoryChapter.All.Where(row => row.StoryId == "maknae").OrderBy(row => row.Id).ToArray();
        const GameServerCommand Sweep = GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST;
        GsNormalBattleSkipRequest Skip(int id, uint count = 1) => new() { stageId = (uint)id, skipCount = count };
        async Task<LocalRankCombatReply> StoryState() => await peer.Request<LocalRankCombatReply>(
            GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST, new LocalRankCombatRequest { operation = LocalStoryChapter.ProgressOperation });
        var initial = (await StoryState()).storyStages.Where(row => row.stage is >= 8611 and <= 8619).ToArray();
        Check(initial.Length == 9 && initial.Count(row => row.unlocked) == 1 && initial.All(row => row.stars == 0 && !row.canSweep),
            "Maknae chapter lists nine stages and opens only the first");
        uint beforeBlocked = player.Stamina;
        await peer.Request<FailPacket>(Start, Enter(maknae[1]), success: false);
        await peer.Request<FailPacket>(Sweep, Skip(8613), success: false);
        Check(player.Stamina == beforeBlocked && player.ActiveStage == null, "Locked stage and premature sweep charge nothing");
        player.Stamina = 9999;
        for (int i = 0; i < player.CardIds.Count; i++)
            if (catalog.Cards.Any(card => card.Id == player.CardIds[i] && LocalStoryChapter.IsMaknaeFamily(card.EvolveFirstAncestor)))
                player.CardIds[i] = 30081;
        int initialFragments = player.Inventory.GetValueOrDefault((uint)LocalStoryChapter.MaknaeFragmentId);
        var originalMaknae = catalog.Cards.Single(row => row.Id == LocalStoryChapter.MaknaeCardId);
        Check(originalMaknae.Talent == BscTable.CARD_TALENT.CARD_TALENT_SS && originalMaknae.PieceId == LocalStoryChapter.MaknaeFragmentId,
            "Story targets the original SS Maknae family and fragments");
        foreach (var stage in maknae)
        {
            var started = await peer.Request<LocalStoryBattleStartOk>(Start, Enter(stage));
            Check(started.waveProbIdList.SequenceEqual(catalog.StoryWaves[stage.Id]) &&
                started.storyEnemyLevelDelta is >= -5 and <= 12, "Fixed waves and bounded enemy strength at " + stage.Id);
            var finish = Result(stage, started.timeStamp, true);
            finish.cardDieCount = 1;
            var prize = await peer.Request<GsNormalBattleEndConfirmOk>(End, finish);
            Check(prize.earnedCardList.Any(x => x.cardID == LocalStoryChapter.MaknaeCardId) == (stage.Id == 8619),
                "Only the final boss recruits SS Maknae at " + stage.Id);
            if (stage.Id == 8613)
                await peer.Request<FailPacket>(Sweep, Skip(8613), success: false);
        }
        var complete = (await StoryState()).storyStages.Where(row => row.stage is >= 8611 and <= 8619).ToArray();
        Check(complete.All(row => row.unlocked && row.stars == 2) &&
            complete.Where(row => row.canSweep && row.sweepsLeft == 3).Select(row => row.stage).SequenceEqual(new uint[] { 8613, 8616, 8619 }) &&
            player.Inventory[LocalStoryChapter.MaknaeFragmentId] == initialFragments + 18,
            "Nine two-star victories unlock exactly three bosses and the first-clear fragments");
        uint staminaBeforeSweep = player.Stamina;
        int beforeSweep = player.Inventory[LocalStoryChapter.MaknaeFragmentId];
        foreach (var (stageId, fragments) in new[] { (8613, 1), (8616, 2), (8619, 3) })
        {
            var sweep = Skip(stageId, 3);
            var result = await peer.Request<GsNormalBattleSkipOk>(Sweep, sweep);
            Check(result.earnedList.Count == 3 && result.earnedList.All(x => x.earnedItemList.Single().itemCount == fragments &&
                x.earnedItemList.Single().itemID == LocalStoryChapter.MaknaeFragmentId && x.earnedCardList.Count == 0),
                "Every sweep yields configured fragments at " + stageId);
            await peer.Request<GsNormalBattleSkipOk>(Sweep, sweep, retry: true);
            await peer.Request<FailPacket>(Sweep, Skip(stageId), success: false);
        }
        Check(player.Inventory[LocalStoryChapter.MaknaeFragmentId] == beforeSweep + 18 &&
            player.Stamina == staminaBeforeSweep - maknae.Where(row => row.Boss).Sum(row => 3 * catalog.Stages[(uint)row.TemplateId].ConsumeStamina),
            "Retry and daily-limit rejection leave the exact fragment and stamina totals");
        var upgradeStars = await Win(maknae[2]);
        var repeatStars = await Win(maknae[2]);
        Check(upgradeStars.earnedItemList.Single().itemCount == 2 && repeatStars.earnedItemList.Single().itemCount == 1,
            "Three-star reward is granted once after an earlier two-star clear");
        var reloadedStory = new PlayerStore(save, catalog).Find(player.Uid)!;
        Check(reloadedStory.StoryClears.IsSupersetOf(maknae.Select(row => (uint)row.Id)) && reloadedStory.StoryStars[8613] == 3 &&
            reloadedStory.StorySweeps[8619] == 3 && reloadedStory.CardIds.Count(id => id == LocalStoryChapter.MaknaeCardId) == 1,
            "Maknae card, highest stars, and sweep use survive reload");
        player.StorySweepDay = ProtocolRouter.TowerDay(ProtocolRouter.Now) - 1;
        var reset = (await StoryState()).storyStages.Single(row => row.stage == 8619);
        Check(reset.canSweep && reset.sweepsLeft == 3 && reset.stars == 2, "Daily sweep reset keeps permanent stars");
        Console.WriteLine($"STORY_CHAPTERS_PASSED {checks.Count}");
    }
}
