#if BNSM_SERVER
extern alias game;
using game::BscTable;
#else
using BscTable;
using DTO;
#endif
using System;

internal static class LocalAdventureChapter53
{
    public const int PreviousStage = 179;
    public const int FirstStage = 180;
    public const int LastStage = 185;
    public const int Field = 20;
    public const string FieldName = "白桦林";
    public const string MapPrefab = "BG_FieldLocalBirch";
    public const string BattlePrefab = "BG_BattleLocalBirch";

    public static bool Enabled =>
        Environment.GetEnvironmentVariable("BNSM_CHAPTER53_PREVIEW") == "1";

    internal sealed class Chapter
    {
        public readonly int Id, TemplateId, EpisodeNumber, BossId;
        public readonly string Title, Objective;
        public readonly bool HasDialogue;

        public Chapter(int id, int template, int episodeNumber, string title, string objective,
            bool hasDialogue = true, int boss = 0)
        {
            Id = id;
            TemplateId = template;
            EpisodeNumber = episodeNumber;
            HasDialogue = hasDialogue;
            Title = hasDialogue ? $"第{episodeNumber}章 {title}" : title;
            Objective = objective;
            BossId = boss;
        }

        public BattleStageLink MakeLink(BattleStageLink template)
        {
            var stage = LocalHongCard.Copy(template);
            stage.Id = Id;
            stage.FieldId = Field;
            stage.EpId = EpisodeNumber;
            stage.EpSubId = Id - FirstStage + 1;
            stage.OpenStageId = Id - 1;
            stage.EpisodeFileGroupId = HasDialogue ? stage.EpId + 1 : 0;
            string episode = "LocalChapter" + stage.EpId;
            stage.EpisodeStartFile = HasDialogue ? episode + "_Start" : string.Empty;
            stage.EpisodeEndFile = HasDialogue ? episode + "_End" : string.Empty;
            stage.EpisodeBgmSet = HasDialogue ? "EP_Drama" : string.Empty;
            stage.DungeonNameAlias = Title;
            stage.DescAlias = Objective;
            stage.EpisodeBossMobId = BossId;
            stage.GenRaidMobId = stage.RaidBossId = 0;
            return stage;
        }

        public BattleStageInfo MakeBattle(BattleStageInfo template)
        {
            var battle = LocalHongCard.Copy(template);
            battle.Id = Id;
            battle.StageBg = BattlePrefab;
            battle.RaidBossId = 0;
            if (BossId != 0)
            {
                foreach (var wave in battle.WaveList)
                    foreach (var mob in wave.MobCardInfoList)
                        if (mob.CardId == BossId)
                        {
                            wave.BossWave = true;
                            mob.BossMob = mob.MainMob = mob.ShowCard = true;
                        }
            }
            battle.Update();
            return battle;
        }
    }

    public static readonly Chapter[] Chapters =
    {
        new Chapter(180, 178, 53, "碎器余响", "击退沿着脚印追来的敌人，循着法器残片去找老小。"),
        new Chapter(181, 176, 53, "断桥截哨", "过桥前击退白桦林外的巡哨。", hasDialogue: false),
        new Chapter(182, 177, 54, "林中两路", "打退守着岔路的斥候，辨认被调换过的路标。"),
        new Chapter(183, 175, 55, "雨夜截车", "截住往北渠去的押运队，拿到夹在车板里的货单。"),
        new Chapter(184, 173, 55, "北渠伏兵", "扫清藏在渠口的伏兵，再沿车辙往林深处走。", hasDialogue: false),
        new Chapter(185, 174, 56, "桦林深处", "击败押运首领，打开囚车并救出驿卒。", boss: 8017433),
    };

    public static Chapter Find(int id) =>
        Enabled && id >= FirstStage && id <= LastStage ? Chapters[id - FirstStage] : null;

    public static int FieldLimit(CONTENT_OPEN_TYPE type, int original)
    {
        if (!Enabled) return original;
        switch (type)
        {
            case CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_NORMAL:
                return Field;
            case CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_HARD:
            case CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_LEGEND:
                return Math.Min(original, Field - 1);
            default:
                return original;
        }
    }

#if !BNSM_SERVER
    public static bool IsEpisodeScene()
    {
        return Enabled && MainController.GetGameSceneController()?.gameSceneType == GAME_SCENE_TYPE.GAME_SCENE_EPISODE;
    }

#endif
}
