extern alias game;
using ProtoBuf;

internal sealed class GameCatalog
{
    public readonly bool RepairPlaceholderCards;
    private readonly HashSet<uint> unavailableCardIds;
    public IEnumerable<BscTable.CardData> UsableHeroes => Cards.Where(c=>CardIdentityPolicy.IsCollectible(c) && !unavailableCardIds.Contains((uint)c.Id) && !LocalOriginalCardDefinition.IsCard(c.Id));
    public IEnumerable<BscTable.CardData> InitialHeroes => UsableHeroes.Where(c => LocalCardRegistry.FindExtensionForCard(c.Id)==null).GroupBy(c=>c.EvolveFirstAncestor)
        .Select(g=>g.OrderBy(c=>c.Rank).ThenBy(c=>c.Id).First());
    /// <summary>
    /// 寻宝卡池：每条进化链取 <paramref name="rank"/> 阶那一张，再按资质分池。
    /// 星级口径由 <see cref="GachaRules.CardRank"/> 决定（当前 4 星）。
    /// 单点定义，测试与诊断都从这里取，避免"文档写四星、代码出五星"这类漂移。
    /// </summary>
    public BscTable.CardData[] DrawPool(bool cash,int rank)=>UsableHeroes.Where(c=>c.Rank==rank && LocalCardRegistry.FindExtensionForCard(c.Id)==null && c.Talent<=BscTable.CARD_TALENT.CARD_TALENT_S)
        .Where(c=>cash ? c.Talent>=BscTable.CARD_TALENT.CARD_TALENT_A : c.Talent<=BscTable.CARD_TALENT.CARD_TALENT_A).ToArray();
    public static readonly uint TutorialEndStage = (uint)game::BSC_DEFINE.TUTORIAL_END_STAGE;
    /// <summary>普通第 4 大关通关后才开困难模式（客户端 BSC_DEFINE.NORMAL_FIELD_ID_TO_UNLOCK_HARD_MODE）。</summary>
    public static readonly int NormalFieldToUnlockHard = game::BSC_DEFINE.NORMAL_FIELD_ID_TO_UNLOCK_HARD_MODE;
    /// <summary>困难第 3 大关通关后才开传说模式（客户端 BSC_DEFINE.HARD_FIELD_ID_TO_UNLOCK_LEGEND_MODE）。</summary>
    public static readonly int HardFieldToUnlockLegend = game::BSC_DEFINE.HARD_FIELD_ID_TO_UNLOCK_LEGEND_MODE;
    /// <summary>NumberTable 里的 MAX_PVE_STAGE_ID，为 0 时回落到 BSC_DEFINE.MAX_STAGE_ID。</summary>
    public uint MaxPveStage {
        get {
            if (LocalAdventureChapter53.Enabled) return LocalAdventureChapter53.LastStage;
            double configured = Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.MAX_PVE_STAGE_ID);
            return configured > 0 ? (uint)configured : (uint)game::BSC_DEFINE.MAX_STAGE_ID;
        }
    }
    public uint FirstAdventureStage => Stages.Keys.Where(id => id > TutorialEndStage).Min();
    public readonly List<BscTable.ChallengeTowerData> Towers;
    public readonly List<BscTable.ChallengeStageLink> TowerStages;
    public readonly List<BscTable.BattleStageInfo> TowerWaves;
    public readonly List<BscTable.ChallengeTowerBanGroupData> TowerBans;
    public readonly List<BscTable.ChallengePointRewardData> TowerPointRewards;
    public readonly IReadOnlyList<BscTable.CardData> Cards;
    private readonly LocalCardRegistry.CardCatalog cardCatalog;
    public BscTable.CardData? FindCard(long id)=>id is >=int.MinValue and <=int.MaxValue?cardCatalog.Get((int)id):null;
    public BscTable.CardData RequireCard(long id)=>FindCard(id)??throw new InvalidDataException("Unknown card ID: "+id);
    public readonly List<BscTable.PieceData> Pieces;
    public readonly Dictionary<BscTable.GAME_DIFFICULTY, List<BscTable.FieldAchievementRateReward>> FieldRewards = [];
    public readonly Dictionary<BscTable.GAME_DIFFICULTY, Dictionary<uint,BscTable.BattleStageLink>> Dungeons = [];
    private readonly Dictionary<(BscTable.GAME_DIFFICULTY,uint),List<uint>> dungeonWaves = [];
    public readonly Dictionary<uint, BscTable.BattleStageLink> Stages;
    public readonly Dictionary<uint, BscTable.BattleStageLink> HardStages;
    public readonly Dictionary<uint, BscTable.BattleStageLink> LegendStages;
    // 保留表内原始顺序：客户端 GetNextLegendStageID 就是按 List 顺序扫描的，排序会改变结果。
    public readonly List<BscTable.BattleStageLink> NormalStageRows;
    public readonly List<BscTable.BattleStageLink> HardStageRows;
    public readonly List<BscTable.BattleStageLink> LegendStageRows;
    /// <summary>普通表的 FieldId → 该大关内的关卡 Id（升序）。客户端 __fieldMap 的正统来源。</summary>
    public readonly Dictionary<int, List<uint>> NormalFieldStages;
    public readonly List<BscTable.GachaSetData> GachaSets;
    public readonly List<BscTable.CriticalGoldData> CriticalGold;
    public readonly List<BscTable.PackageData> Packages;
    public readonly List<BscTable.GoodsData> Goods;
    public readonly List<BscTable.ShopSetData> Shops;
    public readonly List<BscTable.SystemShopSetData> SystemShops;
    public readonly List<BscTable.SystemShopExpenseWeightData> SystemShopPrices;
    public readonly List<BscTable.CashProductData> CashProducts;
    public readonly List<BscTable.BuyCashData> BuyCashProducts;
    public readonly List<BscTable.CardSkinData> Skins;
    public readonly List<BscTable.HelperData> Helpers;
    public readonly List<BscTable.HelperRewardData> HelperRewards;
    public readonly List<BscTable.PlayerStateData> PlayerStates;
    public readonly List<BscTable.CardLevelExpData> CardLevels;
    public readonly List<BscTable.CardRankData> CardRanks;
    public readonly List<BscTable.TitleData> Titles;
    public readonly List<BscTable.ExpShopSetData> ExpShop;
    public readonly List<BscTable.MoneyTypeResource> MoneyResources;
    public readonly List<BscTable.PromotionMinigameData> PubPromotions;
    public readonly List<BscTable.MinigameSetData> PubGames;
    public readonly List<BscTable.EvilWarData> EvilWarNumbers;
    public readonly List<BscTable.EvilWarRuneData> EvilWarRunes;
    public readonly List<BscTable.EvilWarPcData> EvilWarPlayers;
    public readonly List<BscTable.TrainData> Training;
    public readonly List<BscTable.TranscendenceData> Transcendence;
    public readonly List<BscTable.FormationData> Formations;
    public readonly List<BscTable.FormationExpenseData> FormationExpenses;
    public readonly List<BscTable.RelicData> Relics;
    public readonly List<BscTable.CardResetData> CardResets;
    public readonly List<BscTable.CardResetRewardData> CardResetRewards;
    public readonly List<BscTable.TrainExpData> TrainingExp;
    public readonly List<BscTable.VipData> Vip;
    public readonly List<BscTable.ExchangeData> Exchanges;
    public readonly List<BscTable.ItemData> Items;
    public readonly Dictionary<int,BscTable.ItemData> ItemsById;
    public readonly List<BscTable.ContentData> Contents;
    public readonly BscTable.ItemCardProcessDataTable CardProcesses;
    public readonly List<BscTable.CardEnchantData> CardEnchants;
    public readonly HashSet<int> SkillIds;
    public readonly Dictionary<int,BscTable.SkillData> Skills;
    public readonly Dictionary<int,BscTable.SkillEnchantData> SkillEnchants;
    public readonly List<BscTable.RecipeData> Recipes;
    public readonly List<BscTable.BoxData> Boxes;
    public readonly List<BscTable.DropGroupData> DropGroups;
    public readonly BscTable.ExpenseWeightDataTable Expenses;
    public readonly Dictionary<BscTable.NUMBER_TYPE,double> Numbers;
    private readonly Dictionary<(BscTable.GAME_DIFFICULTY difficulty, uint id), BscTable.BattleStageInfo> stageWaves = new();
    internal readonly Dictionary<int, uint[]> StoryWaves = [];
    internal readonly Dictionary<int, LocalStoryChapter> StoryForgeSources = [];
    public readonly List<string> MissingStageFields = new();
    public GameCatalog(string root,bool? repairPlaceholderCards=null,bool enableOriginalCardPrototype=false)
    {
        string rules=Path.Combine(root,"LocalServer/data/local-rules.json");
        bool configured=false;
        if(File.Exists(rules)) {
            using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(rules));
            configured=document.RootElement.TryGetProperty("repairPlaceholderCards",out var enabled) && enabled.GetBoolean();
        }
        RepairPlaceholderCards=repairPlaceholderCards ?? configured;
        using(var availability=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/card-resource-availability.json")))) {
            string digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables/CardDataTable.pb")))).ToLowerInvariant();
            if(availability.RootElement.GetProperty("cardTableSha256").GetString()!=digest)throw new InvalidDataException("Card asset availability index does not match the original card table");
            unavailableCardIds=availability.RootElement.GetProperty("excludedCardIds").EnumerateArray().Select(v=>v.GetUInt32()).ToHashSet();
        }
        T Read<T>(string name)
        {
            using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", name + ".pb"));
            return Serializer.Deserialize<T>(input);
        }
        Towers=Read<BscTable.ChallengeTowerDataTable>("ChallengeTowerTable").List;
        TowerStages=Read<BscTable.ChallengeStageLinkTable>("ChallengeStageTable").List;
        TowerWaves=Read<BscTable.BattleStageInfoTable>("ChallengeStageField").List;
        TowerBans=Read<BscTable.ChallengeTowerBanGroupDataTable>("ChallengeTowerBanGroupTable").List;
        TowerPointRewards=Read<BscTable.ChallengePointRewardDataTable>("ChallengePointRewardTable").List;
        var nativeCards = Read<BscTable.CardDataTable>("CardDataTable").List;
        var nativeCardsById = nativeCards.GroupBy(card=>card.Id).ToDictionary(group=>group.Key,group=>group.First());
        if(enableOriginalCardPrototype&&unavailableCardIds.Contains(LocalOriginalCardDefinition.SourceCardId))
            throw new InvalidDataException("Original-card source rig is unavailable");
        cardCatalog = new LocalCardRegistry.CardCatalog(nativeCardsById.Keys,
            (id, _) => nativeCardsById.GetValueOrDefault(id), includeCustom: true, includePrototype: enableOriginalCardPrototype);
        Cards = cardCatalog.Ids.Select(id=>RequireCard(id)).ToList().AsReadOnly();
        Boxes=Read<BscTable.BoxDataTable>("BoxDataTable").List;
        DropGroups=Read<BscTable.DropGroupDataTable>("DropGroupDataTable").List;
        Pieces = Read<BscTable.PieceDataTable>("PieceDataTable").List;
        foreach (var entry in LocalCardRegistry.Entries)
        {
            if (nativeCards.Any(c => c.CollectionId == entry.CollectionId) ||
                Pieces.Any(piece => piece.ItemId == entry.FragmentId))
                throw new InvalidDataException($"Registered card, collection or recipe collision: {entry.Name}");
            Pieces.Add(entry.CreatePiece(Pieces.Single(piece => piece.ItemId == entry.FragmentTemplateId).Complete));
        }
        GachaSets = Read<BscTable.GachaSetDataTable>("GachaSetDataTable").List;
        GachaSets.AddRange(Read<BscTable.GachaSetDataTable>("VIPGachaSetDataTable").List);
        CriticalGold = Read<BscTable.CriticalGoldDataTable>("CriticalGoldDataTable").List;
        Packages = Read<BscTable.PackageDataTable>("PackageDataTable").List;
        Goods = Read<BscTable.GoodsDataTable>("GoodsDataTable").List;
        Shops = Read<BscTable.ShopSetDataTable>("ShopSetDataTable").List;
        SystemShops = Read<BscTable.SystemShopSetDataTable>("SystemShopSetDataTable").List;
        SystemShopPrices = Read<BscTable.SystemShopExpenseWeightDataTable>("SystemShopExpenseDataTable").List;
        CashProducts = Read<BscTable.CashProductDataTable>("CashProductDataTable").List;
        BuyCashProducts = Read<BscTable.BuyCashDataTable>("BuyCashDataTable").List;
        Skins = Read<BscTable.CardSkinDataTable>("CardSkinDataTable").List;
        Helpers = Read<BscTable.HelperDataTable>("HelperDataTable").List;
        HelperRewards = Read<BscTable.HelperRewardDataTable>("HelperRewardDataTable").List;
        PlayerStates = Read<BscTable.PlayerStateDataTable>("PlayerStateData").List;
        CardLevels = Read<BscTable.CardLevelExpDataTable>("CardLevelExpTable").List;
        CardRanks = Read<BscTable.CardRankDataTable>("CardRankDataTable").List;
        Titles = Read<BscTable.TitleDataTable>("TitleDataTable").List;
        ExpShop = Read<BscTable.ExpShopSetDataTable>("ExpShopSetDataTable").List;
        MoneyResources = Read<BscTable.MoneyTypeResourceTable>("MoneyTypeResource").List;
        PubPromotions = Read<BscTable.PromotionMinigameDataTable>("PromotionMinigameDataTable").List;
        PubGames = Read<BscTable.MinigameSetDataTable>("MinigameSetDataTable").List;
        EvilWarNumbers = Read<BscTable.EvilWarDataTable>("EvilWarDataTable").List;
        EvilWarRunes = Read<BscTable.EvilWarRuneDataTable>("EvilWarRuneDataTable").List;
        EvilWarPlayers = Read<BscTable.EvilWarPcDataTable>("EvilWarPcDataTable").List;
        Training = Read<BscTable.TrainDataTable>("TrainingcampTable").List;
        Transcendence = Read<BscTable.TranscendenceDataTable>("TranscendenceDataTable").List;
        foreach (var row in LocalCardRegistry.TranscendenceRows(id=>Transcendence.Single(original=>original.CardId==id)))
        {
            if (Transcendence.Any(original=>original.CardId==row.CardId))
                throw new InvalidDataException("Registered transcendence card ID collision: "+row.CardId);
            Transcendence.Add(row);
        }
        Formations = Read<BscTable.FormationDataTable>("FormationTable").List;
        FormationExpenses = Read<BscTable.FormationExpenseDataTable>("FormationExpenseTable").List;
        Relics = Read<BscTable.RelicDataTable>("RelicDataTable").List;
        CardResets = Read<BscTable.CardResetDataTable>("CardResetDataTable").List;
        CardResetRewards = Read<BscTable.CardResetRewardDataTable>("CardResetRewardDataTable").List;
        TrainingExp = Read<BscTable.TrainExpDataTable>("TrainingcampExpTable").List;
        Vip = Read<BscTable.VipDataTable>("VIPDataTable").List;
        Exchanges = Read<BscTable.ExchangeDataTable>("ExchangeDataTable").List;
        Items = Read<BscTable.ItemDataTable>("ItemDataTable").List;
        foreach (string table in new[] { "EquipmentDataTable", "GemDataTable", "EmpowerItemDataTable", "CardSkinItemTable", "HelperClothesDataTable", "EventItemDataTable" })
            Items.AddRange(Read<BscTable.ItemDataTable>(table).List);
        if(Items.Any(x=>LocalHelperCostumeDefinition.IsItem(x.Id)))throw new InvalidDataException("Local helper outfit ID collision");
        for(int i=0;i<=LocalHelperCostumeDefinition.MaxEnchant;i++) {
            int id=LocalHelperCostumeDefinition.FirstItemId+i;
            Items.Add(LocalHelperCostumeDefinition.CreateItem(id,Items.Single(x=>x.Id==LocalHelperCostumeDefinition.TemplateId(id))));
        }
        foreach(var helper in Helpers)LocalHelperCostumeDefinition.AddModels(helper);
        foreach (var entry in LocalCardRegistry.Entries)
        {
            if (Items.Any(item => item.Id == entry.FragmentId))
                throw new InvalidDataException($"Registered fragment item collision: {entry.Name}");
            Items.Add(entry.CreateFragment(Items.Single(item => item.Id == entry.FragmentTemplateId)));
        }
        if (Items.Any(x => x.Id == LocalStoryChapter.WeaponBlankId)) throw new InvalidDataException("Story weapon blank item ID collision");
        Items.Add(LocalStoryChapter.MakeWeaponBlank(Items.Single(x => x.Id == LocalStoryChapter.WeaponId)));
        var originalItems = Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var gear in LocalCardRegistry.Equipment(id => originalItems.GetValueOrDefault(id)))
        {
            if (originalItems.ContainsKey(gear.Id)) throw new InvalidDataException($"Registered equipment ID collision: {gear.Id}");
            Items.Add(gear);
        }
        ItemsById=Items.GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.First());
        Contents = Read<BscTable.ContentDataTable>("ContentDataTable").List;
        CardProcesses=Read<BscTable.ItemCardProcessDataTable>("ItemCardProcessData");
        CardEnchants=Read<BscTable.CardEnchantDataTable>("CardEnchantDataTable").List;
        Skills=Read<BscTable.SkillDataTable>("SkillDataTable").List.ToDictionary(x=>x.Id);
        if(enableOriginalCardPrototype){
            if(Skills.Keys.Any(LocalOriginalCardDefinition.IsSkill))throw new InvalidDataException("Original-card skill ID collision");
            for(int i=0;i<2;i++)
            {
                var skill=LocalCardRegistry.CreatePrototypeSkill(LocalOriginalCardDefinition.FirstSkillId+i,id=>nativeCardsById[id],id=>Skills[id]);
                Skills.Add(skill.Id,skill);
            }
        }
        foreach (var entry in LocalCardRegistry.Entries)
        {
            for (int id = entry.FirstSkillId; id < entry.FirstSkillId + entry.SkillCount; id++)
            {
                if (Skills.ContainsKey(id)) throw new InvalidDataException($"Registered skill collision: {entry.Name}/{id}");
                Skills.Add(id, entry.CreateSkill(id, original => Skills[original], original => nativeCardsById[original]));
            }
        }
        SkillIds=Skills.Keys.ToHashSet();
        SkillEnchants=Read<BscTable.SkillEnchantDataTable>("SkillEnchantDataTable").List.ToDictionary(x=>x.SkillEnchantGroupID);
        foreach (var group in LocalCardRegistry.EnchantGroups(id => SkillEnchants[id]))
            SkillEnchants.Add(group.SkillEnchantGroupID, group);
        Recipes=Read<BscTable.RecipeDataTable>("RecipeDataTable").List;
        Expenses = Read<BscTable.ExpenseWeightDataTable>("ExpenseWeightDataTable"); Expenses.MakeMap();
        Numbers = Read<BscTable.NumberDataTable>("NumberTable").List.ToDictionary(x => x.Alias,x => x.Num);
        NormalStageRows = Read<BscTable.BattleStageLinkTable>("BattleStageTable_Normal").List;
        if (LocalAdventureChapter53.Enabled)
            foreach (var chapter in LocalAdventureChapter53.Chapters)
                NormalStageRows.Add(chapter.MakeLink(NormalStageRows.Single(row => row.Id == chapter.TemplateId)));
        Stages = NormalStageRows.GroupBy(x => (uint)x.Id).ToDictionary(g => g.Key, g => g.First());
        HardStageRows = ReadPlayableStageRows(root, "BattleStageTable_Hard");
        HardStages = HardStageRows.GroupBy(x => (uint)x.Id).ToDictionary(g => g.Key, g => g.First());
        LegendStageRows = ReadPlayableStageRows(root, "BattleStageTable_Legend");
        LegendStages = LegendStageRows.GroupBy(x => (uint)x.Id).ToDictionary(g => g.Key, g => g.First());
        NormalFieldStages = NormalStageRows.GroupBy(x => x.FieldId)
            .ToDictionary(g => g.Key, g => g.Select(x => (uint)x.Id).Distinct().OrderBy(x => x).ToList());
        foreach ((BscTable.GAME_DIFFICULTY difficulty, string suffix) in new[]
                 {
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, "Normal"),
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, "Hard"),
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, "Legend"),
                 })
        {
            FieldRewards[difficulty] = Read<BscTable.FieldAchievementRateRewardTable>("FieldAchievementRateRewardTable_" + suffix).List;
            Dungeons[difficulty]=ReadPlayableStageRows(root,"DungeonStageTable_"+suffix).ToDictionary(x=>(uint)x.Id);
            foreach(int field in Dungeons[difficulty].Values.Select(x=>x.FieldId).Distinct()) {
                string file=$"DungeonStage_Field{field}_{suffix}";
                if(!File.Exists(Path.Combine(root,"LocalServer/data/tables",file+".pb"))) {MissingStageFields.Add(file);continue;}
                foreach(var dungeon in Read<BscTable.DungeonStageInfoTable>(file).List)
                    dungeonWaves[(difficulty,(uint)dungeon.Id)]=dungeon.FloorList.FirstOrDefault()?.WaveList
                        .GroupBy(x=>x.Id).OrderBy(g=>g.Key).Select(g=>(uint)g.First().ProbId).ToList()??[];
            }
            foreach (int field in StagesFor(difficulty).Values.Select(x => x.FieldId).Distinct())
            {
                if (LocalAdventureChapter53.Enabled && field == LocalAdventureChapter53.Field) continue;
                string file = $"BattleStage_Field{field}_{suffix}";
                if (!File.Exists(Path.Combine(root, "LocalServer/data/tables", file + ".pb")))
                {
                    MissingStageFields.Add(file);
                    continue;
                }
                foreach (var stage in Read<BscTable.BattleStageInfoTable>(file).List)
                    stageWaves[(difficulty, (uint)stage.Id)] = stage;
            }
        }
        if (LocalAdventureChapter53.Enabled)
        {
            const BscTable.GAME_DIFFICULTY normal = BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL;
            foreach (var chapter in LocalAdventureChapter53.Chapters)
                stageWaves.Add((normal, (uint)chapter.Id), chapter.MakeBattle(stageWaves[(normal, (uint)chapter.TemplateId)]));
        }
        foreach (var chapter in LocalStoryChapter.All)
        {
            const BscTable.GAME_DIFFICULTY normal = BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL;
            if (Stages.ContainsKey((uint)chapter.Id) || !Stages.ContainsKey((uint)chapter.TemplateId) ||
                !stageWaves.TryGetValue((normal, (uint)chapter.TemplateId), out var template) ||
                chapter.RequiredHero != 0 && !Cards.Any(card => card.Id == chapter.RequiredHero))
                throw new InvalidDataException($"Invalid story stage or prerequisite hero: {chapter.Id}");
            var waves = template.WaveList.GroupBy(wave => wave.Id).OrderBy(group => group.Key).ToArray();
            if (waves.Length == 0 || chapter.WaveIds.Length != 0 &&
                (chapter.WaveIds.Length != waves.Length || waves.Where((wave, index) => !wave.Any(row => row.ProbId == chapter.WaveIds[index])).Any()))
                throw new InvalidDataException($"Invalid fixed story waves: {chapter.Id}");
            StoryWaves.Add(chapter.Id, chapter.WaveIds.Length == 0
                ? waves.Select(wave => (uint)wave.First().ProbId).ToArray()
                : chapter.WaveIds.Select(id => (uint)id).ToArray());
            foreach (var reward in new[] { chapter.FirstReward, chapter.ReplayReward, chapter.ThreeStarReward })
                if (reward.Card != 0 && !UsableHeroes.Any(card => card.Id == reward.Card) ||
                    reward.Items.Any(item => !ItemsById.ContainsKey(item.Id)))
                    throw new InvalidDataException($"Unknown story reward: {chapter.Id}");
            foreach (int id in chapter.FirstReward.EquipmentBlanks)
            {
                if (!ItemsById.TryGetValue(id, out var blank) || blank.ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT ||
                    blank.Next1Id <= 0 || !ItemsById.ContainsKey(blank.Next1Id) ||
                    blank.Material1Id.Count != blank.Material1Value.Count ||
                    blank.Material1Id.Any(material => !ItemsById.ContainsKey(material)) ||
                    !StoryForgeSources.TryAdd(id, chapter))
                    throw new InvalidDataException($"Invalid or duplicate story equipment blank: {chapter.Id}/{id}");
            }
        }
        System.Console.WriteLine($"Catalog: {Cards.Count} original cards, {Stages.Count} normal battle stages.");
        System.Console.WriteLine($"Difficulty stages: normal={Stages.Count}, hard={HardStages.Count}, legend={LegendStages.Count}" +
            (MissingStageFields.Count > 0 ? $", missing field tables={string.Join(",", MissingStageFields)}" : ""));
    }

    /// <summary>
    /// 读一张难度关卡链表，并剔除"空占位行"（表里存在 Id 但所有数值都是 0 的行）。
    /// 原始 Hard 表有 3 行、Legend 表有 109 行这种全零行，它们不可进入也不该出现在解锁链里。
    /// </summary>
    private static List<BscTable.BattleStageLink> ReadPlayableStageRows(string root, string table)
    {
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", table + ".pb"));
        return Serializer.Deserialize<BscTable.BattleStageLinkTable>(input).List
            .Where(x => x.Id > 0 && (x.ConsumeStamina > 0 || x.Gold > 0 || x.RewardExp > 0 || x.DailyLimit > 0))
            .ToList();
    }

    public Dictionary<uint, BscTable.BattleStageLink> StagesFor(BscTable.GAME_DIFFICULTY difficulty) => difficulty switch
    {
        BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD => HardStages,
        BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => LegendStages,
        _ => Stages,
    };

    public List<uint> StarterCards() => InitialHeroes.Where(x => x.Id > 0 && !string.IsNullOrEmpty(x.Character)).Select(x=>(uint)x.Id)
        .Take(5).ToList();
    public Dictionary<uint,BscTable.BattleStageLink> BattleStages(BscTable.GAME_DIFFICULTY difficulty,bool dungeon)=>dungeon?Dungeons[difficulty]:StagesFor(difficulty);
    public List<uint> WaveIds(BscTable.GAME_DIFFICULTY difficulty, uint stage,bool dungeon=false) =>
        dungeon ? dungeonWaves.GetValueOrDefault((difficulty,stage),[]) : stageWaves.TryGetValue((difficulty, stage), out var info)
            ? info.WaveList.GroupBy(x => x.Id).OrderBy(x => x.Key).Select(g => (uint)g.First().ProbId).ToList()
            : [];
    public uint StaminaMax(uint level) => (uint)Math.Max(1,PlayerStates.First(x => x.Level == level).MaxStamina);
    public BscTable.VipData VipFor(uint exp) => Vip.Where(x => x.LvUpNeedVipExp <= exp).OrderByDescending(x => x.VipLv).First();
    public uint SkillPointMax(LocalPlayer p) => (uint)Math.Max(0,PlayerStates.First(x=>x.Level==p.Level).MaxSkillPoint+VipFor(p.VipExp).SkillPointBonusCount);
    public int ContentValue(BscTable.CONTENT_OPEN_TYPE type) {
        var content=Contents.FirstOrDefault(x=>x.Alias==type);
        int i=content?.VersionId.FindIndex(v=>(int)v==0) ?? -1;
        int value = i>=0 && (int)content!.OpenState[i]==1 ? content.ContentValue[i] : int.MaxValue;
        return LocalAdventureChapter53.FieldLimit(type, value);
    }
}
