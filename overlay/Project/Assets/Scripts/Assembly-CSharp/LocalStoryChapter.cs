#if BNSM_SERVER
extern alias game;
using game::BscTable;
using System.Text.Json;
#else
using BscTable;
using UnityEngine;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[Serializable]
public sealed class LocalStoryChapter
{
    internal const string BattleArtPrefix = "LocalStoryBattle_";
    public const int MaknaeCardId = 30181;
    public const int MaknaeFragmentId = 205081313;
    public const int MaknaeActOneFirstStage = 8621;
    public const int MaknaeActOneBoss = 8625;
    public const int MaknaeFinalStage = 8636;
    public const int HongPreviewFirstStage = 8641;
    public const int HongForgePreviewFirstStage = 8661;
    public const int LegacyHongForgeStage = 8604;
    public static bool MaknaeActOnePreview => Environment.GetEnvironmentVariable("BNSM_MAKNAE_ACT_ONE_PREVIEW") == "1";
    public static bool HongPreview => Environment.GetEnvironmentVariable("BNSM_HONG_PREVIEW") == "1";
    public static int MaknaeFirstStage => MaknaeActOnePreview ? MaknaeActOneFirstStage : 8611;
    public const uint ProgressOperation = 15;
    private const int MaknaeCardOffer = 2000000276;
    private const int MaknaeFragmentOffer = MaknaeCardOffer + 1;

    public static bool IsMaknaeOffer(int id) => id == MaknaeCardOffer || id == MaknaeFragmentOffer;

    internal static IEnumerable<GoodsData> MaknaeMarketPreviews()
    {
        yield return new GoodsData { GoodsId = MaknaeCardOffer, ProductId = MaknaeCardId,
            ProductIdType = GOODS_TYPE.GOODS_TYPE_CARD, NameAlias = "SS 老小", Quantity = 1, PcLevelMin = 1, PcLevelMax = 999 };
        yield return new GoodsData { GoodsId = MaknaeFragmentOffer, ProductId = MaknaeFragmentId,
            ProductIdType = GOODS_TYPE.GOODS_TYPE_ITEM, NameAlias = "SS 老小碎片", Quantity = 1, PcLevelMin = 1, PcLevelMax = 999 };
    }

    public const int WeaponBlankId = 1900041002;
    public const int WeaponId = 109074038;
    public const int WeaponOwnerId = 30161;
    public const string NightSakuraFirstClaim = "900011/1";

    public static LocalStoryChapter Hong => Find(HongPreview ? HongPreviewFirstStage : 8601);
    public static LocalStoryChapter Weapon => Find(8602);
    public static LocalStoryChapter Outfit => Find(8603);
    public static LocalStoryChapter HongForge => Find(HongPreview ? HongForgePreviewFirstStage : LegacyHongForgeStage);

    private static readonly Lazy<Dictionary<int, LocalStoryChapter>> Catalog =
        new Lazy<Dictionary<int, LocalStoryChapter>>(Load);

    public int Id, TemplateId, Level, RequiredHero, PreviousStage, Act;
    public string Line = "", MenuLabel = "", Title = "", Opening = "", Scene = "", Goal = "", RequiredHeroName = "";
    public string StoryId = "", StoryTitle = "", MapArt = "", BattleArt = "";
    public bool Boss, BattleOnly, ShareRepeatRewardLimit;
    public bool HongForgeReward;
    public int SweepAfterStage, DailySweeps;
    public Reward ThreeStarReward = new Reward();
    public int[] WaveIds = Array.Empty<int>();
    public int EnemyLevelReference, EnemyLevelMaxIncrease, EnemyLevelMaxDecrease;
    public Reward FirstReward = new Reward(), ReplayReward = new Reward();

    [Serializable]
    public sealed class Reward
    {
        public int Card;
        public string ClaimKey = "";
        public RewardItem[] Items = Array.Empty<RewardItem>();
        public int[] EquipmentBlanks = Array.Empty<int>();
    }

    [Serializable]
    public sealed class RewardItem
    {
        public int Id, Count;
    }

    [Serializable]
    public sealed class Definition
    {
        public int Version;
        public LocalStoryChapter[] Stages = Array.Empty<LocalStoryChapter>();
        public LocalStoryChapter[] MaknaeActOnePreviewStages = Array.Empty<LocalStoryChapter>();
        public LocalStoryChapter[] HongPreviewStages = Array.Empty<LocalStoryChapter>();
    }

    public static IEnumerable<LocalStoryChapter> All => Catalog.Value.Values;

    public static LocalStoryChapter Find(int id) =>
        Catalog.Value.TryGetValue(id, out var stage) ? stage : null;

    public static bool IsMaknaeFamily(int ancestor) => ancestor == MaknaeCardId;

    public int EnemyLevelDelta(int teamLevel) => EnemyLevelReference == 0 ? 0 :
        Math.Max(-EnemyLevelMaxDecrease, Math.Min(EnemyLevelMaxIncrease, (teamLevel - EnemyLevelReference) / 5));

    private static Dictionary<int, LocalStoryChapter> Load()
    {
#if BNSM_SERVER
        using var stream = typeof(LocalStoryChapter).Assembly.GetManifestResourceStream("LocalStoryCatalog.json")
            ?? throw new InvalidDataException("Missing embedded story catalog");
        var definition = JsonSerializer.Deserialize<Definition>(stream, new JsonSerializerOptions { IncludeFields = true });
#else
        var asset = Resources.Load<TextAsset>("LocalStoryCatalog");
        if (asset == null) throw new InvalidDataException("Missing story catalog asset");
        var definition = JsonUtility.FromJson<Definition>(asset.text);
#endif
        if (MaknaeActOnePreview && definition?.Stages != null)
        {
            if (definition.MaknaeActOnePreviewStages == null || definition.MaknaeActOnePreviewStages.Length == 0)
                throw new InvalidDataException("Missing Maknae act one preview stages");
            definition.Stages = definition.Stages.Where(stage => stage?.StoryId != "maknae")
                .Concat(definition.MaknaeActOnePreviewStages).ToArray();
        }
        if (HongPreview && definition?.Stages != null)
        {
            if (definition.HongPreviewStages == null || definition.HongPreviewStages.Length == 0)
                throw new InvalidDataException("Missing Hong preview stages");
            definition.Stages = definition.Stages.Where(stage => stage.Id != 8601 && stage.Id != LegacyHongForgeStage)
                .Concat(definition.HongPreviewStages).ToArray();
        }
        return Validate(definition);
    }

    internal static Dictionary<int, LocalStoryChapter> Validate(Definition definition)
    {
        if (definition == null || definition.Version != 1 || definition.Stages == null || definition.Stages.Length == 0)
            throw new InvalidDataException("Unsupported or empty story catalog");
        var stages = new Dictionary<int, LocalStoryChapter>();
        foreach (var stage in definition.Stages)
        {
            if (stage == null || stage.Id <= 0 || stage.TemplateId <= 0 || stage.Level <= 0 || stage.RequiredHero < 0 || stage.PreviousStage < 0 ||
                stage.Line != "Character" && stage.Line != "Weapon" && stage.Line != "Outfit" ||
                string.IsNullOrWhiteSpace(stage.MenuLabel) || string.IsNullOrWhiteSpace(stage.Title) ||
                string.IsNullOrWhiteSpace(stage.Opening) || string.IsNullOrWhiteSpace(stage.Scene) || string.IsNullOrWhiteSpace(stage.Goal) ||
                string.IsNullOrWhiteSpace(stage.BattleArt) || stage.BattleArt.Any(c => !char.IsLetterOrDigit(c)) ||
                stage.RequiredHero != 0 && string.IsNullOrWhiteSpace(stage.RequiredHeroName) || stages.ContainsKey(stage.Id))
                throw new InvalidDataException("Invalid or duplicate story stage: " + stage?.Id);
            stage.WaveIds ??= Array.Empty<int>();
            if (stage.WaveIds.Any(id => id <= 0) || stage.EnemyLevelReference < 0 || stage.EnemyLevelReference > 99 ||
                stage.EnemyLevelMaxIncrease < 0 || stage.EnemyLevelMaxIncrease > 20 || stage.EnemyLevelMaxDecrease < 0 || stage.EnemyLevelMaxDecrease > 20 ||
                stage.EnemyLevelReference == 0 && (stage.EnemyLevelMaxIncrease != 0 || stage.EnemyLevelMaxDecrease != 0))
                throw new InvalidDataException("Invalid story battle settings: " + stage.Id);
            if (stage.Act < 0 || stage.Act > 0 && string.IsNullOrWhiteSpace(stage.StoryId) ||
                stage.DailySweeps < 0 || stage.DailySweeps > 10 || stage.SweepAfterStage < 0 ||
                stage.ShareRepeatRewardLimit && stage.DailySweeps == 0 ||
                stage.DailySweeps > 0 && (!stage.Boss || stage.SweepAfterStage == 0 || string.IsNullOrWhiteSpace(stage.StoryId)))
                throw new InvalidDataException("Invalid story sweep settings: " + stage.Id);
            stage.ThreeStarReward = ValidateReward(stage.ThreeStarReward, stage.Id);
            if (stage.ThreeStarReward.Card != 0 || stage.ThreeStarReward.EquipmentBlanks.Length != 0)
                throw new InvalidDataException("Invalid story three-star reward: " + stage.Id);
            stage.FirstReward = ValidateReward(stage.FirstReward, stage.Id);
            stage.ReplayReward = ValidateReward(stage.ReplayReward, stage.Id);
            if (stage.ReplayReward.Card != 0 || stage.ReplayReward.EquipmentBlanks.Length != 0)
                throw new InvalidDataException("Recruitment and forging blanks require a first-clear reward: " + stage.Id);
            stages.Add(stage.Id, stage);
        }
        foreach (var stage in stages.Values)
        {
            if (stage.SweepAfterStage != 0 && (!stages.TryGetValue(stage.SweepAfterStage, out var sweepGate) ||
                sweepGate.StoryId != stage.StoryId || stage.ReplayReward.Items.Length == 0))
                throw new InvalidDataException("Invalid story sweep prerequisite: " + stage.Id);
            var visited = new HashSet<int> { stage.Id };
            int previous = stage.PreviousStage;
            while (previous != 0)
            {
                if (!visited.Add(previous) || !stages.TryGetValue(previous, out var prerequisite))
                    throw new InvalidDataException("Invalid story prerequisite: " + stage.Id);
                previous = prerequisite.PreviousStage;
            }
        }
        return stages;
    }

    private static Reward ValidateReward(Reward reward, int stage)
    {
        reward ??= new Reward();
        reward.Items ??= Array.Empty<RewardItem>();
        reward.EquipmentBlanks ??= Array.Empty<int>();
        if (reward.Card < 0 || reward.Items.Any(item => item == null || item.Id <= 0 || item.Count <= 0) ||
            reward.EquipmentBlanks.Any(id => id <= 0) || reward.EquipmentBlanks.Distinct().Count() != reward.EquipmentBlanks.Length)
            throw new InvalidDataException("Invalid story reward: " + stage);
        return reward;
    }

    public BattleStageLink MakeLink(BattleStageLink template)
    {
        var stage = LocalHongCard.Copy(template);
        stage.Id = Id;
        stage.RequirePcLevel = Level;
        stage.OpenStageId = 0;
        stage.DailyLimit = 0;
        stage.EpisodeFileGroupId = stage.GenRaidMobId = stage.RaidBossId = 0;
        stage.EpisodeStartFile = stage.EpisodeEndFile = string.Empty;
        stage.DescAlias = Goal;
        stage.DungeonNameAlias = Title;
        stage.RewardExp = stage.CardRewardExp = stage.Gem = 0;
        stage.NpcRewardId.Clear();
        stage.NpcRewardIdType.Clear();
        stage.NpcRewardIdAlias.Clear();
        stage.RewardQuantity.Clear();
        stage.DropProb.Clear();
        stage.FirstNpcRewardId.Clear();
        stage.FirstNpcRewardIdType.Clear();
        stage.FirstNpcRewardIdAlias.Clear();
        stage.FirstRewardQuantity.Clear();
        stage.FirstDropProb.Clear();
        foreach (var item in ReplayReward.Items)
        {
            stage.NpcRewardId.Add(item.Id);
            stage.NpcRewardIdType.Add(NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM);
            stage.RewardQuantity.Add(item.Count);
            stage.DropProb.Add(1);
        }
        return stage;
    }

    internal BattleStageInfo MakeBattle(BattleStageInfo template)
    {
        var battle = LocalHongCard.Copy(template);
        battle.Id = Id;
        battle.StageBg = BattleArtPrefix + BattleArt;
        battle.RaidBossId = 0;
        if (Boss)
        {
            int lastWave = battle.WaveList.Max(wave => wave.Id);
            foreach (var wave in battle.WaveList.Where(wave => wave.Id == lastWave))
            {
                var leader = wave.MobCardInfoList.OrderByDescending(mob => mob.BossMob)
                    .ThenByDescending(mob => mob.MainMob).ThenByDescending(mob => mob.Level).FirstOrDefault();
                if (leader == null) continue;
                wave.BossWave = true;
                leader.BossMob = leader.MainMob = leader.ShowCard = true;
            }
        }
        battle.Update();
        return battle;
    }

    public static ItemData MakeWeaponBlank(ItemData original)
    {
        var item = LocalHongCard.Copy(original);
        item.Id = WeaponBlankId;
        item.IdAlias = "Item.Local.JimoGauntletBlank";
        item.NameAlias = "旧炉拳甲坯";
        item.DescAlias = "从旧炉中取回的拳甲坯。用副本取得的锻材锻造成究极极魔拳甲。";
        item.Class = CARD_CLASS.CLASS_NONE;
        item.Next1Id = WeaponId;
        item.Next1IdAlias = original.IdAlias;
        item.AttributeValueId.Clear();
        item.AttributeValueIdType.Clear();
        item.AttributeValueIdAlias.Clear();
        item.AttributeUnit.Clear();
        return item;
    }
}
