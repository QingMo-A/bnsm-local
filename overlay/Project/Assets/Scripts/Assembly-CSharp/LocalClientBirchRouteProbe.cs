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
    private IEnumerator WaitForNativePreview(Func<bool> ready, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    private static bool BirchMapFillsViewport(FieldMap map)
    {
        var scenery = map.fieldGroup.GetComponentsInChildren<MeshRenderer>();
        if (scenery.Length != 1 || scenery[0].sharedMaterial?.mainTexture?.name != "WhiteBirchAdventure" || !scenery[0].isVisible)
            return false;
        var backdrop = scenery[0].transform;
        var camera = MainController.GetGameSceneController().fieldGroupLink.gameObject.GetComponent<FieldScene>()
            .fieldCamera.GetComponent<Camera>();
        var surface = new Plane(backdrop.forward, backdrop.position);
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        {
            Ray corner = camera.ViewportPointToRay(new Vector3(x, y, 0));
            if (!surface.Raycast(corner, out float depth)) return false;
            Vector3 point = backdrop.InverseTransformPoint(corner.GetPoint(depth));
            if (Mathf.Abs(point.x) > .501f || Mathf.Abs(point.y) > .501f) return false;
        }
        return true;
    }

    private IEnumerator CheckBirchRoute()
    {
        const string outputOption = "-bnsm-birch-route-output=";
        string option = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith(outputOption, StringComparison.Ordinal));
        if (option != null) folder = Path.GetFullPath(option.Substring(outputOption.Length));
        Directory.CreateDirectory(folder);
        var checks = new Dictionary<string, bool>();
        var seenWaves = new HashSet<int>();
        const GAME_DIFFICULTY normal = GAME_DIFFICULTY.DIFFICULTY_NORMAL;
        bool InField(int id) => MainController.GetGameSceneController()?.gameSceneType == GAME_SCENE_TYPE.GAME_SCENE_FIELD &&
            MonoSingleton<MainController>.Instance.fieldSceneManager.currentFieldID == id &&
            MonoSingleton<MainController>.Instance.fieldSceneManager.FieldMap?.MakeFieldObjectsDone == true &&
            !MainController.GetGameSceneController().sceneFieldTransition;

        yield return WaitForNativePreview(() => MainController.GetGameSceneController()?.gameSceneType == GAME_SCENE_TYPE.GAME_SCENE_TOWN, 75);
        for (int i = 0; i < 8; i++)
        {
            foreach (var notice in UnityEngine.Object.FindObjectsOfType<NoticeDialog>()) notice.On1();
            foreach (var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>()) notice.DoHide();
            LocalGamePanel.Instance?.CloseNotice();
            yield return new WaitForSecondsRealtime(.2f);
        }
        var world = UnityEngine.Object.FindObjectsOfType<TownBuilding>().FirstOrDefault(building => building.name == "WorldWar");
        checks["worldWarIcon"] = world != null && world.iconOC_WorldWar != null &&
            world.iconOC_WorldWar.GetComponentsInChildren<UISprite>().Any(sprite => sprite.spriteName == "WorldWar" && sprite.isActiveAndEnabled);
        if (world != null) world.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
        yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<WorldWarDialog>() != null, 8);
        checks["worldWarLobbyFromTown"] = UnityEngine.Object.FindObjectOfType<WorldWarDialog>() != null;
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "world-war-lobby.png"));
        yield return null;
        Dialog.CloseAllSpace();
        MonoSingleton<MainController>.Instance.OnPVE();
        yield return WaitForNativePreview(() => InField(19) || InField(LocalAdventureChapter53.Field), 25);
        var manager = MonoSingleton<MainController>.Instance.fieldSceneManager;
        if (InField(LocalAdventureChapter53.Field))
        {
            yield return new WaitForSecondsRealtime(1);
            manager.GetFieldUI().OnNextField(-1);
            yield return WaitForNativePreview(() => InField(19), 15);
        }
        checks["previousChapter"] = InField(19);
        if (InField(19))
        {
            checks["birchBackdropScopedToItsOwnField"] = manager.FieldMap.fieldGroup.GetComponentsInChildren<MeshRenderer>()
                .All(renderer => renderer.sharedMaterial?.mainTexture?.name != "WhiteBirchAdventure");
            yield return new WaitForSecondsRealtime(1);
            manager.GetFieldUI().OnNextField(1);
        }
        yield return WaitForNativePreview(() => InField(LocalAdventureChapter53.Field), 20);
        checks["newChapterFromCity"] = InField(LocalAdventureChapter53.Field);
        if (checks["newChapterFromCity"])
        {
            yield return WaitForNativePreview(() => !PrefabSingleton<NetworkStatus>.Instance.LoadingTransitionStarted, 15);
            foreach (var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>()) notice.DoHide();
            yield return new WaitForSecondsRealtime(.6f);
            checks["birchBackdropVisibleAndCoversViewport"] = BirchMapFillsViewport(manager.FieldMap);
            var spots = manager.FieldMap.spotUIs;
            checks["sixNativeSpots"] = spots.Select(spot => spot.index).SequenceEqual(LocalAdventureChapter53.Chapters.Select(chapter => chapter.Id));
            bool ShowsRouteNumber(SpotUI spot) => spot.spot.GetComponent<StageIndicator>().numGo.GetComponent<UISprite>().spriteName ==
                "Field_indicator_" + (spot.index - LocalAdventureChapter53.FirstStage + 1).ToString("00");
            checks["consecutiveRouteNumbers"] = spots.Count == 6 && spots.All(ShowsRouteNumber);
            checks["fiveNativePaths"] = spots.Take(spots.Count - 1).All(spot => spot.fieldNode.spot.Line != null &&
                spot.fieldNode.spot.Line.activeInHierarchy);
            checks["lastStageLockedInitially"] = spots.Last().spot.GetComponent<StageIndicator>().lockGo.activeInHierarchy;
            var bossMarker = spots.Last().spot.GetComponent<StageIndicator>().frameUI?.GetComponent<BossStageSpotUI>();
            checks["nativeBossMarker"] = bossMarker != null && bossMarker.outerFrameGo.activeInHierarchy;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "birch-route.png"));
            yield return null;
            int originalWidth = Screen.width, originalHeight = Screen.height;
            Screen.SetResolution(1584, 680, false);
            yield return WaitForNativePreview(() => Screen.width == 1584 && Screen.height == 680, 3);
            yield return new WaitForSecondsRealtime(.4f);
            checks["birchBackdropCoversUltrawide"] = Screen.width == 1584 && Screen.height == 680 && BirchMapFillsViewport(manager.FieldMap);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "birch-route-ultrawide.png"));
            yield return null;
            Screen.SetResolution(originalWidth, originalHeight, false);
            yield return WaitForNativePreview(() => Screen.width == originalWidth && Screen.height == originalHeight, 3);

            foreach (var chapter in LocalAdventureChapter53.Chapters)
            {
                int id = chapter.Id;
                bool StageOpened() => UnityEngine.Object.FindObjectOfType<EpisodePlayer>() != null ||
                    UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() != null;
                if (id == LocalAdventureChapter53.FirstStage)
                {
                    var spot = manager.FieldMap.spotUIs.Single(node => node.index == id);
                    var character = manager.GetFieldCharacter();
                    if (character.CurrentStage != id)
                    {
                        character.OnStage(spot.spot);
                        yield return WaitForNativePreview(() => character != null && character.CurrentStage == id && !character.Moving, 12);
                    }
                    if (!StageOpened() && character != null) character.OnStage(spot.spot);
                }
                yield return WaitForNativePreview(StageOpened, 22);
                var opening = UnityEngine.Object.FindObjectOfType<EpisodePlayer>();
                checks["openingMatchesStage" + id] = (opening != null) == chapter.HasDialogue;
                if (opening != null)
                {
                    yield return WaitForNativePreview(() => opening.dialogContentRoot.activeInHierarchy, 7);
                    var backdrop = GameObject.Find("WhiteBirchForestBackdrop")?.GetComponent<UITexture>();
                    checks["illustratedOpening" + id] = opening.dialogContentRoot.activeInHierarchy &&
                        backdrop?.mainTexture?.name == "WhiteBirchForest" && backdrop.gameObject.activeInHierarchy;
                    checks["expandedOpening" + id] = opening.GetComponentInChildren<EpisodePlayerChapter>()
                        ?.GetComponents<EpisodePlayerDataDialog>().Length >= 6;
                    if (id == LocalAdventureChapter53.LastStage)
                    {
                        yield return new WaitForSecondsRealtime(.8f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "birch-boss-opening.png"));
                    }
                    yield return null;
                    opening.StopEpisode();
                }
                yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() != null, 12);
                var preview = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>();
                checks[(id == LocalAdventureChapter53.FirstStage ? "firstStagePreview" : "continuedToPreview") + id] =
                    preview?.coEnterStageNormal?.coLabelTitle.text == chapter.Title;
                if (preview == null) break;
                preview.coEnterStageNormal.SendMessage("OnEnterDeck", SendMessageOptions.RequireReceiver);
                yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<BscDeckDialog>() != null, 12);
                var deck = UnityEngine.Object.FindObjectOfType<BscDeckDialog>();
                if (deck == null) break;
                deck.OnNext();
                float deadline = Time.realtimeSinceStartup + 90;
                var enemyCards = Array.Empty<UiEnemyCard>();
                BattleStage battle = null;
                int detachedBars = 0;
                var wavesWithVisibleBars = new HashSet<int>();
                Transform clearing = null, mist = null, foreground = null;
                Camera stageCamera = null;
                bool layersVisible = false, tookBattleScreenshot = false;
                float minParallax = float.PositiveInfinity, maxParallax = float.NegativeInfinity;
                while (Time.realtimeSinceStartup < deadline && UnityEngine.Object.FindObjectOfType<UiBattleResultWin>() == null)
                {
                    if (battle == null) battle = UnityEngine.Object.FindObjectOfType<BattleStage>();
                    if (battle != null && battle.CurrentStage == id && battle.UiManager?.IsLoaded == true)
                    {
                        battle.IsAuto = true;
                        if (enemyCards.Length == 0) enemyCards = battle.UiManager.GetComponentsInChildren<UiEnemyCard>(true);
                        if (enemyCards.Length == 0)
                            enemyCards = battle.UiManager.CenterCard.GetComponentsInChildren<UiEnemyCard>(true);
                        if (clearing == null)
                        {
                            var scene = battle.UiManager.Scene;
                            stageCamera = scene.GetStageCamera();
                            var art = scene.GetComponentInChildren<LocalBirchBattleBackdrop>();
                            clearing = art?.transform.Find("BattleMap/BirchBattleClearing");
                            mist = art?.transform.Find("BattleMap/BirchBattleMist");
                            foreground = art?.transform.Find("BattleMap/BirchBattleForeground");
                            if (clearing != null && mist != null && foreground != null)
                            {
                                var rearMat = clearing.GetComponent<MeshRenderer>().sharedMaterial;
                                var mistMat = mist.GetComponent<MeshRenderer>().sharedMaterial;
                                var frontMat = foreground.GetComponent<MeshRenderer>().sharedMaterial;
                                checks["threeBattleDepthLayers" + id] = clearing.localPosition.z > mist.localPosition.z &&
                                    mist.localPosition.z > foreground.localPosition.z && rearMat.mainTexture.name == "WhiteBirchBattle" &&
                                    frontMat.mainTexture.name == "WhiteBirchBattleForeground" && mistMat.GetFloat("_Mist") > 0 &&
                                    frontMat.GetFloat("_Softness") > 0 && mistMat.shader.isSupported && frontMat.shader.isSupported;
                            }
                        }
                        if (clearing != null && mist != null && foreground != null && stageCamera != null && stageCamera.isActiveAndEnabled)
                        {
                            float separation = stageCamera.WorldToViewportPoint(clearing.position).x -
                                stageCamera.WorldToViewportPoint(foreground.position).x;
                            minParallax = Mathf.Min(separation, minParallax);
                            maxParallax = Mathf.Max(separation, maxParallax);
                            layersVisible |= clearing.GetComponent<MeshRenderer>().isVisible && mist.GetComponent<MeshRenderer>().isVisible &&
                                foreground.GetComponent<MeshRenderer>().isVisible;
                            if (!tookBattleScreenshot && (id == LocalAdventureChapter53.FirstStage || id == LocalAdventureChapter53.LastStage) &&
                                wavesWithVisibleBars.Count > 0 && battle.BattleWaveTime > .8f)
                            {
                                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "birch-battle-" + id + ".png"));
                                tookBattleScreenshot = true;
                            }
                        }
                        foreach (var enemy in enemyCards)
                        {
                            if (enemy.CardHp?.hpSlider?.gameObject.activeInHierarchy != true) continue;
                            bool visible = enemy.cardImage != null && enemy.cardImage.gameObject.activeInHierarchy &&
                                LocalBattleHealthLayout.ArtRect(enemy, NGUITools.FindCameraForLayer(enemy.cardImage.gameObject.layer), out Rect art) &&
                                art.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
                            if (visible) wavesWithVisibleBars.Add(battle.CurrentWave);
                            else detachedBars++;
                        }
                    }
                    yield return new WaitForEndOfFrame();
                }
                var win = UnityEngine.Object.FindObjectOfType<UiBattleResultWin>();
                checks["battleWon" + id] = win != null;
                checks["battleLayersVisible" + id] = layersVisible;
                checks["battleParallax" + id] = maxParallax - minParallax > .003f;
                int expectedWaves = Singleton<DataTable>.Instance.LoadBattleStageInfo(id, normal).WaveList.Count;
                checks["healthFollowsCards" + id] = detachedBars == 0 && wavesWithVisibleBars.Count == expectedWaves;
                if (id == LocalAdventureChapter53.FirstStage) seenWaves.UnionWith(wavesWithVisibleBars);
                if (win == null) break;
                UIButtonMessage NextButton() => win.GetComponentsInChildren<UIButtonMessage>().FirstOrDefault(button =>
                    button.name == "Btn_Next" && button.functionName == "OnExitBtn" && button.isActiveAndEnabled);
                yield return WaitForNativePreview(() => NextButton() != null, 6);
                var nextButton = NextButton();
                checks["resultArrowWorks" + id] = nextButton != null;
                if (nextButton == null) break;
                nextButton.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
                yield return WaitForNativePreview(() => UnityEngine.Object.FindObjectOfType<EpisodePlayer>() != null || InField(LocalAdventureChapter53.Field), 15);
                var ending = UnityEngine.Object.FindObjectOfType<EpisodePlayer>();
                checks["endingMatchesStage" + id] = (ending != null) == chapter.HasDialogue;
                if (ending != null)
                {
                    yield return WaitForNativePreview(() => ending.dialogContentRoot.activeInHierarchy, 8);
                    ending.StopEpisode();
                }
                yield return WaitForNativePreview(() => InField(LocalAdventureChapter53.Field), 18);
                yield return new WaitForSecondsRealtime(.25f);
                checks["returnedToRoute" + id] = InField(LocalAdventureChapter53.Field);
                if (checks["returnedToRoute" + id])
                    checks["birchBackdropAfterReturn" + id] = BirchMapFillsViewport(manager.FieldMap);
                if (id < LocalAdventureChapter53.LastStage && checks["returnedToRoute" + id])
                {
                    var nextSpot = manager.FieldMap.spotUIs.Single(node => node.index == id + 1);
                    var indicator = nextSpot.spot.GetComponent<StageIndicator>();
                    bool NextSpotUnlocked() => indicator != null && !indicator.lockGo.activeSelf && indicator.numGo.activeSelf && ShowsRouteNumber(nextSpot);
                    yield return WaitForNativePreview(NextSpotUnlocked, 2.75f);
                    checks["nextSpotUnlocked" + id] = NextSpotUnlocked();
                }
            }
            yield return WaitForNativePreview(() => InField(LocalAdventureChapter53.Field) &&
                !PrefabSingleton<NetworkStatus>.Instance.LoadingTransitionStarted, 9);
            if (InField(LocalAdventureChapter53.Field))
            {
                checks["finalResultStopsAtMap"] = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() == null;
                yield return new WaitForSecondsRealtime(2f);
                checks["allUnlockedNumbersVisible"] = manager.FieldMap.spotUIs.All(spot =>
                    spot.spot.GetComponent<StageIndicator>().numGo.activeInHierarchy && ShowsRouteNumber(spot));
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "birch-route-completed.png"));
                yield return new WaitForSecondsRealtime(.25f);
            }
        }
        checks["finishedAllChapters"] = MonoSingleton<MainClient>.Instance.Player.GetClearStageID(normal) == LocalAdventureChapter53.LastStage;
        bool passed = checks.Values.All(value => value) && checks["finishedAllChapters"];
        File.WriteAllText(Path.Combine(folder, "birch-route.json"),
            LitJson.JsonMapper.ToJson(new { passed, errors, checks, firstStageObservedWaves = seenWaves.OrderBy(wave => wave).ToArray() }));
        Application.Quit(passed ? 0 : 4);
    }
}
