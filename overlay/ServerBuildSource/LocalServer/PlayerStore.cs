using System.Text.Json;

internal sealed class LocalPlayer
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,JsonElement>? ExtraFields { get; set; }
    public LocalActivityState Activities {get;set;}=new();
    public HashSet<uint> StoryClears {get;set;}=[];
    public Dictionary<uint, byte> StoryStars {get;set;}=[];
    public Dictionary<uint, uint> StorySweeps {get;set;}=[];
    public ulong StorySweepDay {get;set;}
    public LocalFactionState Faction {get;set;}=new();
    public LocalCompetitiveState Competitive {get;set;}=new();
    public LocalChainBattleState ChainBattle {get;set;}=new();
    public LocalMailbox Mailbox {get;set;}=new();
    public bool BsBallUseFlag {get;set;}
    public uint ArenaPoint {get;set;}
    public uint RankingPoint {get;set;}
    public Dictionary<string,LocalMutationReceipt> ActivityReceipts {get;set;}=[];
    public LocalBoardState Board {get;set;}=new();
    public LocalResourceState Resources {get;set;}=new();
    public Dictionary<string,LocalMutationReceipt> BoardReceipts {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> ResourceReceipts {get;set;}=[];
    public LocalMissionState Missions {get;set;}=new();
    public Dictionary<string,LocalMutationReceipt> MissionReceipts {get;set;}=[];
    public LocalRanking Ranking {get;set;}=new();
    public LocalFriends Friends {get;set;}=new();
    public LocalGuildUser Guild {get;set;}=new();
    public Dictionary<uint,LocalGuildBotActivity> GuildBotActivities {get;set;}=[];
    public List<LocalGuild> FoundedGuilds {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> GuildReceipts {get;set;}=[];
    public ulong LastActivity {get;set;}
    public uint Uid { get; set; }
    public string Account { get; set; } = "";
    public string Nickname { get; set; } = "本地剑灵";
    public uint SelectedTitle {get;set;}
    public Dictionary<uint,LocalTitle> Titles {get;set;}=[];
    public ulong ExpShopDay {get;set;}
    public Dictionary<uint,uint> ExpShopPurchases {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> ProfileReceipts {get;set;}=[];
    public LocalPubGame PubGame {get;set;}=new();
    public Dictionary<string,LocalMutationReceipt> InnReceipts {get;set;}=[];
    public LocalChallengeTower ChallengeTower {get;set;}=new();
    public Dictionary<string,LocalMutationReceipt> TowerReceipts {get;set;}=[];
    public LocalEvilWar EvilWar {get;set;} = new();
    public Dictionary<string,LocalMutationReceipt> EvilWarReceipts {get;set;}=[];
    public uint Level { get; set; } = 1;
    public uint DebugFlags {get;set;}
    public Dictionary<uint,LocalMarketState> Markets {get;set;}=[];
    public ulong UserSettings { get; set; }
    public uint Exp { get; set; }
    public Dictionary<uint,LocalTrainingSlot> TrainingSlots {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> TrainingReceipts {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> TranscendenceReceipts {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> PolishReceipts {get;set;}=[];
    public Dictionary<int,int> FormationLevels {get;set;}=[];
    public Dictionary<BscTable.GAME_MODE,LocalFormationLoadout> FormationLoadouts {get;set;}=[];
    public List<uint> ClassRelicLevels {get;set;}=[0,0,0,0,0,0,0,0];
    public ulong TrainingHelpDay {get;set;}
    public uint TrainingHelpUsed {get;set;}
    public LocalRaidState Raids {get;set;}=new();
    public LocalWanderState Wander {get;set;}=new();
    public uint GuildTokens {get;set;}
    public uint WorldBossPoint {get;set;}
    public Dictionary<string,LocalMutationReceipt> RaidReceipts {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> WanderReceipts {get;set;}=[];
    public uint Gold { get; set; } = 10000;
    public uint CashPoint { get; set; }
    public uint VipExp { get; set; }
    public uint? SkillPoint {get;set;}
    public ulong SkillPointStamp {get;set;}
    public int SkillPointChargeCount {get;set;}
    public Dictionary<string,LocalMutationReceipt> GrowthReceipts {get;set;}=[];
    // Local profiles already have a nickname, helper and starter deck.
    public NEWBIE_FLAG_TYPE NewbieFlag { get; set; } = NEWBIE_FLAG_TYPE.NEWBIE_FLAG_OLDBIE;
    public Dictionary<uint, LocalGachaProgress> Gacha { get; set; } = [];
    public Dictionary<string,LocalMutationReceipt> GachaReceipts {get;set;}=[];
    public LocalChargeOrder? PendingCharge { get; set; }
    public Dictionary<string,LocalMutationReceipt> CommerceReceipts {get;set;}=[];
    public uint Stamina { get; set; } = 100;
    public ulong StaminaStamp { get; set; }
    public ulong ExchangeDay { get; set; }
    public int StaminaBuyCount { get; set; }
    public int StaminaLiteBuyCount { get; set; }
    public ulong LeaderSequence { get; set; } = 1;
    public uint HelperId { get; set; } = 1;
    public uint HelperLikeAccumulation { get; set; }
    public uint HelperLikeRewardAt { get; set; }
    public Dictionary<string,LocalMutationReceipt> HelperReceipts { get; set; } = [];
    public Dictionary<uint,uint> HelperCostumes { get; set; } = [];
    public string Intro { get; set; } = "";
    public BscTable.USER_FEELING_TYPE Feeling { get; set; }
    public Dictionary<ulong,LocalCardState> CardStates { get; set; } = [];
    public Dictionary<uint,int> Inventory { get; set; } = [];
    public List<LocalOwnedEquipment> EquipmentItems { get; set; } = [];
    public ulong NextEquipmentSequence { get; set; } = 1UL << 40;
    public HashSet<uint> PurchasedVipPackages { get; set; } = [];
    public Dictionary<uint,uint> SystemShopPurchases { get; set; } = [];
    public Dictionary<uint,List<uint>> ShopPages { get; set; } = [];
    public uint GoldExchangeCount { get; set; }
    public int GoldExchangeMisses { get; set; }
    public int GoldCriticalCount { get; set; }
    public int GoldCriticalStreak { get; set; }
    public int GoldCriticalForbid { get; set; }
    public Dictionary<string,LocalMutationReceipt> ShopReceipts { get; set; } = [];
    public Dictionary<string,LocalMutationReceipt> EquipmentReceipts { get; set; } = [];
    public Dictionary<string,LocalMutationReceipt> EquipmentBatchReceipts {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> AdventureReceipts { get; set; } = [];
    public Dictionary<string,LocalMutationReceipt> CraftingReceipts { get; set; } = [];
    public Dictionary<string,LocalMutationReceipt> LootReceipts { get; set; } = [];
    public uint Moonstone { get; set; }
    // One bit per original reward ordinal (1..3), independent for each difficulty.
    public Dictionary<uint,byte> NormalFieldRewardClaims { get; set; } = [];
    public Dictionary<uint,byte> HardFieldRewardClaims { get; set; } = [];
    public Dictionary<uint,byte> LegendFieldRewardClaims { get; set; } = [];
    public Dictionary<uint,List<ulong>> Decks { get; set; } = [];
    public HashSet<uint> CollectedCardIds { get; set; } = [];
    public List<LocalCardIdentityRepair> CardIdentityRepairs {get;set;}=[];
    public uint ClearStage { get; set; } = GameCatalog.TutorialEndStage;
    // Hard/Legend chapters share the normal stage numbering; the original client
    // unlocks clearStage + 1 per difficulty, so both start at the tutorial end.
    public uint ClearHardStage { get; set; } = GameCatalog.TutorialEndStage;
    public uint ClearLegendStage { get; set; } = GameCatalog.TutorialEndStage;
    // Daily entry counters keyed by stage id, reset together with the other UTC+8 daily state.
    public Dictionary<uint, uint> HardBattleCounts { get; set; } = [];
    public Dictionary<uint, uint> LegendBattleCounts { get; set; } = [];
    // VIP 购买的重置次数累计（客户端 GetUsedBattleResetCount），与每日次数一起跨日清零。
    public Dictionary<uint, uint> HardBattleResetCounts { get; set; } = [];
    public Dictionary<uint, uint> LegendBattleResetCounts { get; set; } = [];
    // 关卡星级与完美通关时间，按难度分开存（key = stageId）。
    // 客户端只从 gameInfo 的 achieveInfo / hardAchieveInfo / legendAchieveInfo 读取，
    // 索引是 stage*2（星级）与 stage*2+1（完美时间），所以必须由服务端持久化并每次登录下发，
    // 否则星级每进一次游戏就归零。
    public Dictionary<uint, LocalStageRecord> StageRecords { get; set; } = [];
    public Dictionary<uint, LocalStageRecord> HardStageRecords { get; set; } = [];
    public Dictionary<uint, LocalStageRecord> LegendStageRecords { get; set; } = [];
    public Dictionary<uint, LocalStageRecord> DungeonStageRecords { get; set; } = [];
    public Dictionary<uint, LocalStageRecord> HardDungeonStageRecords { get; set; } = [];
    public Dictionary<uint, LocalStageRecord> LegendDungeonStageRecords { get; set; } = [];
    public Dictionary<uint,uint> HardDungeonBattleCounts { get; set; } = [];
    public Dictionary<uint,uint> HardDungeonResetCounts { get; set; } = [];
    public ulong TutorialStatus { get; set; } = ulong.MaxValue;
    // ── 签到状态 ──
    // 存 UTC+8 的"日序号"而不是时间戳：跨日判断只比整数，不受时区/秒差影响，
    // 也不会因为玩家把系统时间改了几分钟而重复领奖。
    /// <summary>月度签到已领取的格数（对应 loginRewardList 的下标）。</summary>
    public uint AttendanceDays { get; set; }
    /// <summary>最后一次领月度普档的日序号；0 表示从未领过。</summary>
    public ulong AttendanceLastDay { get; set; }
    /// <summary>
    /// 最后一次领取"同格 VIP 加成档"的日序号。
    /// 用它而不是一个布尔标记：VIP 等级可能在领完普档之后才提升，
    /// 布尔标记会把"当时不够 VIP"永久固化，导致当天那一档再也领不到。
    /// </summary>
    public ulong AttendanceVipClaimedDay { get; set; }
    /// <summary>最后一次领签到奖励的 Unix 秒，回包给客户端的 latestRewardTime。</summary>
    public ulong AttendanceLastTime { get; set; }
    /// <summary>短期签到已领天数。</summary>
    public uint ShortTermDays { get; set; }
    /// <summary>最后一次领短期签到的日序号。</summary>
    public ulong ShortTermLastDay { get; set; }
    public uint ClearEpisodeCutScene { get; set; }
    public uint ClearHardEpisodeCutScene { get; set; }
    public uint ClearLegendEpisodeCutScene { get; set; }
    public List<uint> CardIds { get; set; } = [];
    public List<ulong> Deck { get; set; } = [];
    public uint? ActiveStage { get; set; }
    public BscTable.GAME_DIFFICULTY ActiveDifficulty { get; set; }
    public bool ActiveDungeon { get; set; }
    public ulong ActiveBattleStamp { get; set; }
    public ulong LastBattleStamp { get; set; }
}

/// <summary>单关记录：星级（0-3）与完美通关时间（秒，byte 与原客户端一致）。</summary>
internal sealed class LocalStageRecord
{
    public byte Stars { get; set; }
    public byte PerfectTime { get; set; }
}

internal sealed class LocalCardState
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,JsonElement>? ExtraFields { get; set; }
    public uint Level { get; set; } = 1;
    public uint Exp { get; set; }
    public uint Protect { get; set; }
    public uint Color { get; set; }
    public uint Skin { get; set; }
    public uint Enchant {get;set;}
    public List<LocalTranscendenceEffect> TranscendenceEffects {get;set;}=[];
    public LocalTranscendenceUndo? TranscendenceUndo {get;set;}
    public ulong WeaponSequence { get; set; }
    public List<ulong> AccessorySequences { get; set; } = [0,0];
    public List<uint> GemIds { get; set; } = [0,0,0];
    public List<uint> ActiveSkills {get;set;}=[1,1];
    public List<uint> PassiveSkills {get;set;}=[1,1,1,1,1];
    // Protocol order: active 1/2, then passive 1..5; absent in old saves means zero.
    public List<uint> SkillEnchants {get;set;}=[0,0,0,0,0,0,0];
}

internal sealed class LocalOwnedEquipment
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,JsonElement>? ExtraFields { get; set; }
    public uint ItemId { get; set; }
    public ulong Sequence { get; set; }
    public ulong RegisteredAt { get; set; }
    public uint CurrentEmpowerValue {get;set;}
    public uint BsBallId {get;set;}
    public uint BsBallCount {get;set;}
    public bool Locked {get;set;}
    public List<uint> EmpowerValues {get;set;}=[];
}

internal sealed class LocalMutationReceipt
{
    public ulong WorldBattleStamp {get;set;}
    public string Fingerprint {get;set;}="";
    public ushort Command {get;set;}
    public string Name {get;set;}="";
    public string Payload {get;set;}="";
    public string TypeName {get;set;}="";
}

internal sealed class LocalGachaProgress
{
    public uint Drawn { get; set; }
    public uint FreeUsed { get; set; }
    public ulong ResetAt { get; set; }
}

internal sealed class LocalChargeOrder
{
    public string Token { get; set; } = "";
    public uint ProductId { get; set; }
    public uint Cash { get; set; }
    public uint Gold { get; set; }
    public uint? VipExperience {get;set;}
    public Dictionary<uint,int> Items {get;set;}=[];
    public List<uint> Cards {get;set;}=[];
}

internal sealed class PlayerStore
{
    private readonly string path;
    public string DirectoryPath => Path.GetDirectoryName(path)!;
    public readonly object Gate = new();
    private readonly List<LocalPlayer> players;
    private readonly GameCatalog catalog;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    public PlayerStore(string directory, GameCatalog catalog)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "players.json");
        this.catalog = catalog;
        SaveWorkspace.ValidateData(directory);
        if (File.Exists(path))
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            players = JsonSerializer.Deserialize<List<LocalPlayer>>(input, Options)
                ?? throw new InvalidDataException("Player save is empty");
        }
        else players = [];
        if(players.Any(p=>p.Level>ProgressionRules.PlayerMax(catalog)||catalog.PlayerStates.Any(x=>x.Level==p.Level && p.Exp<x.Exp) || p.CardStates.Values.Any(s=>catalog.CardLevels.Any(x=>x.Level==s.Level && x.Rank1>s.Exp)))) {
            File.Copy(path,path+".before-debug-exp-repair-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            foreach(var p in players)ProgressionRules.RepairLegacy(catalog,p);
            Save();
        }
        if(players.Any(p=>p.SkillPoint==null)) {
            File.Copy(path,path+".before-social-growth-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            foreach(var p in players.Where(p=>p.SkillPoint==null)) {
                p.SkillPoint=catalog.SkillPointMax(p);p.SkillPointStamp=ProtocolRouter.Now;
            }
            Save();
        }
        // The original client unlocks exactly clearStage + 1. Local onboarding is
        // skipped, so its original tutorial stages 10..12 must already be clear.
        if (players.Any(p => p.TutorialStatus == ulong.MaxValue && p.ClearStage < GameCatalog.TutorialEndStage))
        {
            File.Copy(path, path + ".before-adventure-fix-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            foreach (var player in players.Where(p => p.TutorialStatus == ulong.MaxValue && p.ClearStage < GameCatalog.TutorialEndStage))
                player.ClearStage = GameCatalog.TutorialEndStage;
            Save();
        }
        var repairs=players.Select(p=>new {Player=p,Plan=catalog.RepairPlaceholderCards ? CardIdentityPolicy.Plan(catalog,p) : []}).Where(x=>x.Plan.Count>0).ToList();
        if(repairs.Count>0) {
            File.Copy(path,path+".before-card-identity-fix-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            foreach(var repair in repairs)foreach(var card in repair.Plan) {
                repair.Player.CardIds[(int)card.Sequence-1]=card.CardId;
                repair.Player.CollectedCardIds.Add(card.CardId);repair.Player.CardIdentityRepairs.Add(card);
                System.Console.WriteLine($"Card identity repair: uid={repair.Player.Uid}, seq={card.Sequence}, {card.PreviousCardId}->{card.CardId}, same rank={card.Rank}, same model={card.Minimi}");
            }
            Save();
        }
    }
    /// <summary>
    /// 一次性数据修复：给"有通关进度但没有星级记录"的老存档补 1 星。
    ///
    /// 星级是本轮才补上的持久字段，老档只有通关进度，客户端会把已通关卡显示成 0 星。
    /// 原规则里胜利至少 1 星（`BattleStage.GetNewAchieve` 不会返回 0），所以补 1 星不是伪造。
    /// 只在服务启动时调用一次，不放进构造函数——测试会用 `new PlayerStore(...)` 做只读比对。
    /// </summary>
    public int BackfillMissingStageStars()
    {
        lock (Gate)
        {
            var starless = players.Where(p => p.StageRecords.Count == 0 &&
                (p.ClearStage > GameCatalog.TutorialEndStage || p.ClearHardStage > GameCatalog.TutorialEndStage ||
                 p.ClearLegendStage > GameCatalog.TutorialEndStage)).ToList();
            if (starless.Count == 0) return 0;
            File.Copy(path, path + ".before-stage-stars-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            foreach (var player in starless) BackfillStageRecords(catalog, player);
            Save();
            return starless.Count;
        }
    }

    /// <summary>
    /// 只遍历该难度真实存在的关卡：传说表 id 稀疏，不能按 clear 值逐个补。
    /// </summary>
    private static void BackfillStageRecords(GameCatalog catalog, LocalPlayer player)
    {
        foreach (var (difficulty, records, clear) in new[]
                 {
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL, player.StageRecords, player.ClearStage),
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD, player.HardStageRecords, player.ClearHardStage),
                     (BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND, player.LegendStageRecords, player.ClearLegendStage),
                 })
            foreach (uint stageId in catalog.StagesFor(difficulty).Keys)
                if (stageId > GameCatalog.TutorialEndStage && stageId <= clear && !records.ContainsKey(stageId))
                    records[stageId] = new LocalStageRecord { Stars = 1 };
    }
    public LocalPlayer GetOrCreate(string account)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Length > 128) throw new InvalidDataException("Invalid local account name");
        lock (Gate)
        {
            var player = players.FirstOrDefault(x => x.Account == account);
            if (player != null) return player;
            player = new LocalPlayer { Uid = players.Count == 0 ? 1001 : players.Max(x => x.Uid) + 1,
                Account = account, CardIds = catalog.StarterCards() };
            player.Deck = Enumerable.Range(1, player.CardIds.Count).Select(i => (ulong)i).ToList();
            players.Add(player); Save(); return player;
        }
    }
    public LocalPlayer? Find(uint uid) { lock (Gate) return players.FirstOrDefault(x => x.Uid == uid); }
    /// <summary>只读快照，给调试台列账号用；外部改字段仍要走 Save()。</summary>
    public IReadOnlyList<LocalPlayer> All() { lock (Gate) return players.ToList(); }
    private int saveBatchDepth;
    private bool savePending;
    public long SaveWrites { get; private set; }
    public long SavedBytes { get; private set; }
    public double LastSaveMilliseconds { get; private set; }
    // Mutations already commit under Gate before replying. Manual/background
    // checkpoints only drain an incomplete commit and sync its existing file;
    // serializing the whole resident/player graph again causes mobile GC stalls.
    public void Checkpoint()
    {
        lock (Gate)
        {
            if (saveBatchDepth != 0) throw new InvalidOperationException("A save batch is still active");
            if (savePending || !File.Exists(path)) Save();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            stream.Flush(true);
        }
    }
    internal IDisposable BatchSaves()
    {
        if (!System.Threading.Monitor.IsEntered(Gate)) throw new InvalidOperationException("Save batches require the player gate");
        saveBatchDepth++;
        return new SaveBatch(this);
    }
    private sealed class SaveBatch(PlayerStore owner) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--owner.saveBatchDepth == 0 && owner.savePending) owner.Save();
        }
    }
    public void Save()
    {
        lock (Gate)
        {
            savePending = true;
            if (saveBatchDepth > 0) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // One immutable snapshot and unique temp file per request. Keep
                // durable replacement before OK; never acknowledge a queued save.
                long bytes;
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 65536))
                {
                    JsonSerializer.Serialize(stream, players, Options);
                    bytes = stream.Position;
                }
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Move(temporary, path, true); break; }
                    // Portable File.Replace also reports a busy destination as
                    // ERROR_UNABLE_TO_REMOVE_REPLACED (1175) on Windows.
                    catch (IOException ex) when (attempt < 8 && (ex.HResult & 0xffff) is 32 or 33 or 1175)
                    { Thread.Sleep(25 * (attempt + 1)); }
                    catch (UnauthorizedAccessException ex) when (attempt < 8 && (ex.HResult & 0xffff) == 5)
                    { Thread.Sleep(25 * (attempt + 1)); }
                }
                savePending = false;
                SaveWrites++;
                SavedBytes += bytes;
            }
            finally
            {
                LastSaveMilliseconds = timer.Elapsed.TotalMilliseconds;
                if (File.Exists(temporary)) try { File.Delete(temporary); } catch (IOException) { }
            }
        }
    }
}
