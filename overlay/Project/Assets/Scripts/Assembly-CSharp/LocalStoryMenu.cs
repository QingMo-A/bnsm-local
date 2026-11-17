using System;
using System.Collections.Generic;
using System.Linq;
using BscTable;
using DTO;
using USER_PACKET;

internal static class LocalStoryMenu
{
    internal static bool Enabled => LocalStoryChapter.MaknaeActOnePreview || LocalStoryChapter.HongPreview;
    internal const string ClosedMessage = "江湖篇章暂未开放。";

    internal static bool IsBlockedStage(uint stageId) =>
        !Enabled && LocalMode.Enabled && stageId <= int.MaxValue && LocalStoryChapter.Find((int)stageId) != null;

    internal static bool IsPreviewOffer(int packageId) =>
        LocalStoryChapter.IsMaknaeOffer(packageId) || LocalCardRegistry.FindMarketPreview(packageId) != null;

    internal static bool RequireOpen()
    {
        if (Enabled) return true;
        NoticeDialog.DoModal(ClosedMessage, null, true, false, "江湖篇章", true);
        return false;
    }

    private static ItemData weaponBlank;

    internal static ItemData WeaponBlank(ItemData template)
    {
        if (weaponBlank == null) weaponBlank = LocalStoryChapter.MakeWeaponBlank(template);
        return weaponBlank;
    }

    private static readonly Dictionary<int, LocalStoryStageProgress> progress = new Dictionary<int, LocalStoryStageProgress>();
    private static int returnStage;
    private static int previewAfterDialogue;

    internal static LocalStoryStageProgress Progress(int stage) => progress.TryGetValue(stage, out var value) ? value : null;

    internal static void Refresh(Action ready)
    {
        if (!RequireOpen()) return;
        MonoSingleton<ClientUpdate>.Instance.DoLocalRank(new LocalRankCombatRequest { operation = LocalStoryChapter.ProgressOperation },
            reply =>
            {
                if (reply.operation != LocalStoryChapter.ProgressOperation)
                {
                    NoticeDialog.DoModal("请同步更新本地服务端，再打开江湖篇章。", null, true, false, "江湖篇章", true);
                    return;
                }
                progress.Clear();
                foreach (var row in reply.storyStages) progress[(int)row.stage] = row;
                ready();
            }, (code, value, extra) => NoticeDialog.DoModal(code, value, extra));
    }

    internal static void Open() => Refresh(() =>
    {
        bool hong = LocalStoryChapter.HongPreview && (!LocalStoryChapter.MaknaeActOnePreview ||
            Environment.GetEnvironmentVariable("BNSM_STORY_PREVIEW_FOCUS") == "hong");
        string story = hong ? "hong" : "maknae";
        var focus = LocalStoryChapter.All.OrderBy(stage => stage.Id)
            .FirstOrDefault(stage => stage.StoryId == story &&
                Progress(stage.Id) is LocalStoryStageProgress state && state.unlocked && state.stars == 0)
            ?? (hong ? LocalStoryChapter.Hong : LocalStoryChapter.Find(LocalStoryChapter.MaknaeFinalStage));
        LocalStoryMap.Open("Character", focus);
    });

    private static void ShowStages(LocalStoryChapter chapter) => LocalStoryMap.Open(chapter.Line, chapter);

    internal static string LockReason(LocalStoryChapter stage)
    {
        var player = MonoSingleton<MainClient>.Instance.Player;
        if (player.NameCard.commonInfo.level < stage.Level)
            return "玩家达到 " + stage.Level + " 级后解锁此关。";
        if (stage.RequiredHero != 0 && !player.cards.Any(card => card?.cardData?.EvolveFirstAncestor == stage.RequiredHero))
            return "先解锁" + stage.RequiredHeroName + "，再来开启这条支线。";
        var prior = LocalStoryChapter.Find(stage.PreviousStage);
        return prior == null ? "关卡暂不可用，请返回江湖篇章刷新进度。" : "请先通关「" + prior.Title + "」。";
    }

    internal static void ShowMaknaeAcquisition() => Refresh(() => ShowStages(LocalStoryChapter.Find(LocalStoryChapter.MaknaeFirstStage)));

    internal static void ReturnFromBattle(int stage)
    {
        if (Enabled) returnStage = stage;
    }

    internal static void OnEnterTown()
    {
        if (!Enabled) return;
        if (previewAfterDialogue != 0)
        {
            var next = LocalStoryChapter.Find(previewAfterDialogue);
            previewAfterDialogue = 0;
            if (next != null) Preview(next);
            return;
        }
        if (returnStage == 0) return;
        var stage = LocalStoryChapter.Find(returnStage);
        returnStage = 0;
        if (stage != null) Refresh(() => ShowStages(stage));
    }

    internal static void ShowAcquisition(LocalCardRegistry.Entry entry)
    {
        if (!RequireOpen()) return;
        NoticeDialog.DoModal(entry.Chapter.Goal, "前往篇章", () => Refresh(() => ShowStages(entry.Chapter)),
            "关闭", null, true, null, entry.Name, true);
    }

    internal static void ShowHongEquipmentAcquisition()
    {
        if (!RequireOpen()) return;
        var chapter = LocalStoryChapter.HongForge;
        NoticeDialog.DoModal(chapter.Goal, "前往篇章", () => Refresh(() => ShowStages(chapter)),
            "关闭", null, true, null, chapter.Title, true);
    }

    internal static void OpenChapter(LocalStoryChapter chapter)
    {
        if (!RequireOpen()) return;
        var player = MonoSingleton<MainClient>.Instance.Player;
        if (player.NameCard.commonInfo.level < chapter.Level)
        {
            NoticeDialog.DoModal("玩家达到 " + chapter.Level + " 级后解锁此篇章。", null, true, false, chapter.Title, true);
            return;
        }
        if (chapter.RequiredHero != 0 && !player.cards.Any(card => card?.cardData?.EvolveFirstAncestor == chapter.RequiredHero))
        {
            NoticeDialog.DoModal("先解锁" + chapter.RequiredHeroName + "，再来开启这条支线。",
                null, true, false, chapter.Title, true);
            return;
        }
        if (chapter.BattleOnly)
        {
            Preview(chapter);
            return;
        }
        if (Episode.PlayStory(chapter, Episode.FileType.Start))
        {
            previewAfterDialogue = chapter.Id;
            LocalStoryMap.CloseForBattlePreview();
            return;
        }
        NoticeDialog.DoModal(chapter.Opening, "继续", () => ShowScene(chapter),
            "返回", () => ShowStages(chapter), true, () => ShowStages(chapter), chapter.Title, true);
    }

    private static void ShowScene(LocalStoryChapter chapter)
    {
        NoticeDialog.DoModal(chapter.Scene, "前往副本", () => Preview(chapter),
            "返回", () => OpenChapter(chapter), true, () => ShowStages(chapter), chapter.Title, true);
    }

    internal static void Preview(LocalStoryChapter chapter)
    {
        if (!RequireOpen()) return;
        LocalStoryMap.CloseForBattlePreview();
        const GAME_DIFFICULTY difficulty = GAME_DIFFICULTY.DIFFICULTY_NORMAL;
        var stage = Singleton<DataTable>.Instance.GetBattleStageLink(chapter.Id, difficulty);
        BscEnterStageDialog.DoModalNormal(difficulty, stage, true,
            _ => Refresh(() => ShowStages(chapter)), -1, true);
    }
}
