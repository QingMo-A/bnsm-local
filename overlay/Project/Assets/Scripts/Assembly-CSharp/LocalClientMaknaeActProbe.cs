using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bsc;
using BscBattle;
using BscTable;
using DTO;
using UnityEngine;

public partial class LocalClientProbe
{
    private IEnumerator CheckMaknaeAct(bool hong = false)
    {
        string previewName = hong ? "hong" : "maknae-act";
        string outputOption = "-bnsm-" + previewName + "-output=";
        string option = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith(outputOption, StringComparison.Ordinal));
        if (option != null) folder = Path.GetFullPath(option.Substring(outputOption.Length));
        Directory.CreateDirectory(folder);
        var checks = new Dictionary<string, bool>();
        var battleScenes = new HashSet<string>();
        var battles = new List<object>();
        yield return WaitForNativePreview(() => MainController.GetGameSceneController()?.gameSceneType == GAME_SCENE_TYPE.GAME_SCENE_TOWN, 75);
        yield return DismissStagePreviewNotices();
        foreach (var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>()) notice.DoHide();
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.6f);

        var start = GameObject.Find("LocalStory");
        checks["townEntry"] = start != null;
        start?.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
        yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<LocalStoryMap>() != null, 10);
        int first = hong ? LocalStoryChapter.HongPreviewFirstStage : LocalStoryChapter.MaknaeActOneFirstStage;
        int recruitAt = hong ? 8648 : LocalStoryChapter.MaknaeFinalStage;
        int hero = hong ? LocalHongCard.FirstCardId : LocalStoryChapter.MaknaeCardId;
        var route = LocalStoryChapter.All.Where(stage => hong ? stage.StoryId == "hong" || stage.StoryId == "hong-forge" :
            stage.StoryId == "maknae").OrderBy(stage => stage.Id).ToArray();
        var map = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
        var player = MonoSingleton<MainClient>.Instance.Player;
        int originalProgress = player.GetClearStageID(GAME_DIFFICULTY.DIFFICULTY_NORMAL);
        checks["firstActOpens"] = map != null && map.FirstStage == first && LocalStoryMenu.Progress(first)?.unlocked == true;
        checks["bossesLockedAtStart"] = route.Where(stage => stage.Boss).All(stage =>
            LocalStoryMenu.Progress(stage.Id)?.unlocked == false && LocalStoryMenu.Progress(stage.Id)?.canSweep == false);
        if (hong) checks["forgeLockedAtStart"] = LocalStoryMenu.Progress(8661)?.unlocked == false;
        for (int step = 0; step < 10 && UnityEngine.Object.FindObjectOfType<BscChatNoticeDialog>() != null; step++)
        {
            foreach (var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>()) notice.DoHide();
            yield return new WaitForSecondsRealtime(.35f);
        }

        foreach (var chapter in route)
        {
            if (map == null) break;
            int id = chapter.Id;
            if (!string.IsNullOrEmpty(chapter.MapArt))
            {
                if (hong && map.FirstStage != id) LocalStoryMap.Open(chapter.Line, chapter);
                if (map.FirstStage != id)
                {
                    foreach (var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>()) notice.DoHide();
                    yield return new WaitForSecondsRealtime(.7f);
                    Physics.SyncTransforms();
                    bool drawer = ClickStoryMap(map, "ChapterToggle/Hit");
                    checks["openChapterDrawer" + chapter.Act] = drawer;
                    yield return new WaitForSecondsRealtime(.45f);
                    Physics.SyncTransforms();
                    if (drawer) ScreenCapture.CaptureScreenshot(Path.Combine(folder, "maknae-drawer-" + chapter.Act + ".png"));
                    checks["selectAct" + chapter.Act] = drawer && ClickStoryMap(map, "StoryChapter" + id + "/Hit") && map.FirstStage == id;
                    yield return new WaitForSecondsRealtime(.35f);
                }
                checks["originalArtwork" + id] = map.GetComponentsInChildren<UITexture>().Any(widget =>
                    widget.mainTexture?.name == chapter.MapArt && widget.isVisible);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "story-map-" + id + ".png"));
                yield return null;
            }
            var hit = map.GetComponentsInChildren<UIEventListener>().FirstOrDefault(row => row.name == "StageHit" && row.transform.parent.name == "StoryStage" + id);
            if (hit == null) break;
            hit.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<EpisodePlayer>() != null ||
                UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() != null, 12);
            var opening = UnityEngine.Object.FindObjectOfType<EpisodePlayer>();
            checks["storyOrCombat" + id] = (opening == null) == chapter.BattleOnly;
            if (opening != null)
            {
                yield return WaitForNativePreview(() => opening.dialogContentRoot.activeInHierarchy, 10);
                checks["dialogueActuallyShown" + id] = opening.dialogContentRoot.activeInHierarchy;
                if (id == first)
                {
                    var scene = opening.GetComponentInChildren<EpisodePlayerChapter>();
                    checks["nativeModels"] = scene != null &&
                        scene.GetComponentsInChildren<EpisodeCharacter>().Count(actor =>
                            actor.GetComponentInChildren<SkinnedMeshRenderer>() != null) >= 2;
                    checks["nativeSound"] = scene?.GetComponent<EpisodePlayerDataSound>()?.sfxCue != null;
                    yield return new WaitForSecondsRealtime(1.5f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, previewName + "-dialogue.png"));
                    yield return null;
                    int lines = scene?.GetComponents<EpisodePlayerDataDialog>().Length ?? 0;
                    float dialogueDeadline = Time.realtimeSinceStartup + 18;
                    int clicks = 0;
                    while (opening != null && opening.isActiveAndEnabled && Time.realtimeSinceStartup < dialogueDeadline)
                    {
                        if (opening.dialogContentRoot.activeInHierarchy)
                        {
                            opening.SendMessage("OnDialogClicked", opening.dialogNextBtnUi, SendMessageOptions.RequireReceiver);
                            clicks++;
                        }
                        yield return new WaitForSecondsRealtime(.16f);
                    }
                    checks["dialogueCompletesWithNext"] = clicks >= lines && (opening == null || !opening.isActiveAndEnabled);
                    if (opening != null && opening.isActiveAndEnabled) opening.StopEpisode();
                }
                else opening.StopEpisode();
            }
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() != null, 12);
            var preview = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>();
            checks["correctBattlePreview" + id] = preview?.coEnterStageNormal?.coLabelTitle.text == chapter.Title;
            if (preview == null) break;
            preview.coEnterStageNormal.SendMessage("OnEnterDeck", SendMessageOptions.RequireReceiver);
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BscDeckDialog>() != null, 10);
            var deck = UnityEngine.Object.FindObjectOfType<BscDeckDialog>();
            if (deck == null) break;
            var cards = player.GetSelectedDeck().cardSeqList.Where(seq => seq != 0).Select(player.GetCard).ToArray();
            if (!hong) checks["sOrLowerDeck" + id] = cards.Length == 5 && cards.All(card => card != null &&
                card.cardData.Talent <= CARD_TALENT.CARD_TALENT_S);
            deck.OnNext();
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BattleStage>()?.UiManager?.IsLoaded == true, 25);
            var battle = UnityEngine.Object.FindObjectOfType<BattleStage>();
            if (battle == null || battle.CurrentStage != id) break;
            var expectedArt = Resources.Load<Texture2D>("LocalStoryArt/" + chapter.BattleArt);
            var properties = new MaterialPropertyBlock();
            bool CorrectBackdrop()
            {
                var backdrop = battle.UiManager?.Scene?.GetComponentInChildren<LocalBirchBattleBackdrop>();
                var surface = backdrop?.transform.Find("BattleMap/BirchBattleClearing")?.GetComponent<Renderer>();
                if (expectedArt == null || surface == null || !surface.isVisible) return false;
                surface.GetPropertyBlock(properties);
                return properties.GetTexture("_MainTex") == expectedArt;
            }
            yield return WaitForNativePreview(CorrectBackdrop, 12);
            checks["chapterBattleArt" + id] = CorrectBackdrop();
            if (battleScenes.Add(chapter.BattleArt))
            {
                yield return WaitForNativePreview(() => battle.BattleWaveTime > .85f, 16);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, chapter.BattleArt + ".png"));
                yield return null;
            }
            battle.IsAuto = true;
            TimeManager.SetBattleScale(5);
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<UiBattleResultWin>() != null ||
                UnityEngine.Object.FindObjectOfType<UiBattleResultLose>() != null, 50);
            TimeManager.SetBattleScale(1);
            var win = UnityEngine.Object.FindObjectOfType<UiBattleResultWin>();
            battles.Add(new { stage = id, level = cards.Max(card => (uint)card.cardLevel), won = win != null,
                wave = battle.CurrentWave, losses = battle.GetDeadCardCount(), seconds = (int)battle.CurrentTotalWaveTime });
            checks["wonNativeBattle" + id] = win != null;
            if (win == null) break;
            win.SendMessage("OnExitBtn", SendMessageOptions.RequireReceiver);
            if (LocalStoryDialogue.HasScene(id, Episode.FileType.End))
            {
                yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<EpisodePlayer>()?.dialogContentRoot.activeInHierarchy == true, 12);
                var ending = UnityEngine.Object.FindObjectOfType<EpisodePlayer>();
                checks["endingDialogue" + id] = ending?.dialogContentRoot.activeInHierarchy == true;
                ending?.StopEpisode();
            }
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<LocalStoryMap>()?.FocusStage == id, 12);
            map = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
            checks["returnedToStage" + id] = map != null && map.FocusStage == id;
            if (chapter.Boss)
                checks["recruitmentMatchesStory" + id] = player.cards.Any(card =>
                    card?.cardData?.EvolveFirstAncestor == hero) == (id >= recruitAt);
            if (hong && id == recruitAt) checks["forgeOpensAfterRecruitment"] = LocalStoryMenu.Progress(8661)?.unlocked == true;
            if (map != null) yield return new WaitForSecondsRealtime(.6f);
        }

        foreach (var chapter in route.Where(stage => stage.Boss))
        {
            int boss = chapter.Id;
            checks["bossSweepUnlockedAfterWin" + boss] = LocalStoryMenu.Progress(boss)?.canSweep == true;
            if (!checks["bossSweepUnlockedAfterWin" + boss]) continue;
            var reward = chapter.ReplayReward.Items.ToDictionary(item => item.Id, item => player.GetItemCount((uint)item.Id));
            LocalStoryMenu.Preview(LocalStoryChapter.Find(boss));
            yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() != null, 8);
            var sweep = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>()?.coEnterStageNormal;
            checks["sweepButton" + boss] = sweep != null && sweep.goSkipButton1.activeInHierarchy;
            if (checks["sweepButton" + boss])
            {
                sweep.SendMessage("OnSkipBattle", 0, SendMessageOptions.RequireReceiver);
                yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<UiBattleResultSkip>() != null, 8);
                checks["sweepGrantsRewards" + boss] = chapter.ReplayReward.Items.All(item =>
                    player.GetItemCount((uint)item.Id) == reward[item.Id] + item.Count) &&
                    UnityEngine.Object.FindObjectOfType<UiBattleResultSkip>() != null && LocalStoryMenu.Progress(boss).sweepsLeft == 2;
            }
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.7f);
        }
        checks["mainAdventureProgressPreserved"] = originalProgress == player.GetClearStageID(GAME_DIFFICULTY.DIFFICULTY_NORMAL);
        checks["completedAllBattles"] = battles.Count == route.Length;
        bool passed = errors == 0 && checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(folder, previewName + ".json"), LitJson.JsonMapper.ToJson(new { passed, errors, checks, battles }));
        Application.Quit(passed ? 0 : 4);
    }
}
