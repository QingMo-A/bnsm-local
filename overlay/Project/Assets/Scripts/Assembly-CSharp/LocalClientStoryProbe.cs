using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BscTable;
using DTO;
using UnityEngine;
using USER_PACKET;

public partial class LocalClientProbe
{
    private static Transform StoryMapTarget(LocalStoryMap map, string suffix) =>
        map == null ? null : map.GetComponentsInChildren<UIEventListener>()
            .Select(listener => listener.transform)
            .FirstOrDefault(node => node.name == suffix || node.parent.name + "/" + node.name == suffix);

    private static bool StoryMapReceivesInput(Transform node)
    {
        if (node == null) return false;
        var camera = NGUITools.FindCameraForLayer(node.gameObject.layer);
        var pos = camera.WorldToScreenPoint(node.position);
        if (!camera.pixelRect.Contains(pos)) return false;
        var hit = new RaycastHit();
        return UICamera.Raycast(pos, ref hit) && hit.collider.gameObject == node.gameObject;
    }

    private static bool ClickStoryMap(LocalStoryMap map, string name)
    {
        var node = StoryMapTarget(map, name);
        if (!StoryMapReceivesInput(node)) return false;
        node.gameObject.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
        return true;
    }

    private static bool StoryPageFillsViewport(LocalStoryMap map)
    {
        if (map == null) return false;
        var surface = map.transform.Find("StoryLayout/MapLandscape");
        if (surface == null) return false;
        var art = surface.GetComponentInChildren<UITexture>();
        var hit = surface.GetComponentInChildren<BoxCollider>();
        if (art == null || hit == null) return false;
        var camera = NGUITools.FindCameraForLayer(art.gameObject.layer);
        if (camera == null) return false;
        var lower = camera.WorldToScreenPoint(art.transform.TransformPoint(new Vector3(-.5f, -.5f, 0)));
        var upper = camera.WorldToScreenPoint(art.transform.TransformPoint(new Vector3(.5f, .5f, 0)));
        var view = camera.pixelRect;
        if (Mathf.Abs(lower.x - view.xMin) > 1 || Mathf.Abs(lower.y - view.yMin) > 1 ||
            Mathf.Abs(upper.x - view.xMax) > 1 || Mathf.Abs(upper.y - view.yMax) > 1) return false;
        foreach (var corner in new[]
        {
            new Vector2(view.xMin + 2, view.yMin + 2), new Vector2(view.xMin + 2, view.yMax - 2),
            new Vector2(view.xMax - 2, view.yMin + 2), new Vector2(view.xMax - 2, view.yMax - 2)
        })
        {
            var raycast = new RaycastHit();
            if (!UICamera.Raycast(corner, ref raycast) || raycast.collider != hit) return false;
        }
        return true;
    }

    private static bool ShowsClosedStoryNotice() =>
        UnityEngine.Object.FindObjectsOfType<NoticeDialog>().Any(dialog => !dialog.GonnaHide &&
            dialog.lbNotice.text == LocalStoryMenu.ClosedMessage && dialog.btnOk.btnDelegate == null &&
            dialog.btnCancel.btnDelegate == null);

    private IEnumerator CheckClosedStory(Dictionary<string, object> checks)
    {
        const GAME_DIFFICULTY normal = GAME_DIFFICULTY.DIFFICULTY_NORMAL;
        var tables = Singleton<DataTable>.Instance;
        var chapter = LocalStoryChapter.Hong;
        var town = UnityEngine.Object.FindObjectOfType<Town>();
        checks["townStoryEntryHidden"] = town != null && !town.GetComponentsInChildren<TownBuilding>(true).Any(building => building.name == "LocalStory");
        MainController.GetGameSceneController().SendMessage("OnLocalStory", SendMessageOptions.RequireReceiver);
        LocalStoryMap.Open("Character", chapter);
        checks["townAndDirectMapBlocked"] = ShowsClosedStoryNotice() && UnityEngine.Object.FindObjectOfType<LocalStoryMap>() == null;
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.7f);

        var nativeStage = tables.GetBattleStageLink(chapter.TemplateId, normal);
        checks["nativeStagesStillResolve"] = nativeStage != null && tables.LoadBattleStageInfo(nativeStage.Id, normal) != null;
        checks["storyStageLinksAndWavesBlocked"] = LocalStoryChapter.All.All(stage =>
            tables.GetBattleStageLink(stage.Id, normal) == null && tables.LoadBattleStageInfo(stage.Id, normal) == null);
        LocalStoryMenu.Preview(chapter);
        var capturedLink = chapter.MakeLink(nativeStage);
        BscEnterStageDialog.DoModalNormal(normal, capturedLink, true, null, -1, true);
        BscDeckDialog.DoModalEpisode(capturedLink, null, normal, true, null, null);
        checks["directPreviewAndDeckBlocked"] = ShowsClosedStoryNotice() &&
            UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>() == null && UnityEngine.Object.FindObjectOfType<BscDeckDialog>() == null;
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.7f);

        var socket = MonoSingleton<Network>.Instance.GetGameSocket();
        var key = MonoSingleton<MainClient>.Instance.SessionKey4Game;
        int blocked = 0;
        Action<ServerErrorCode, uint, ulong> onBlocked = (code, _, __) =>
        {
            if (code == ServerErrorCode.ERROR_NOT_OPEN_CONTENTS) blocked++;
        };
        foreach (var stage in LocalStoryChapter.All)
        {
            socket.RequestNormalBattleStart(0, stage.Id, normal, USER_DECK_INDEX_TYPE.USER_DECK_INDEX_SCENARIO, key, null, onBlocked);
            socket.RequestNormalBattleSkip(false, (uint)stage.Id, normal, 1, false, key, null, onBlocked);
        }
        checks["everyStoryStageStartAndSweepBlockedBeforeSending"] = blocked == LocalStoryChapter.All.Count() * 2;

        foreach (int id in new[] { LocalHongCard.FirstCardId, LocalStoryChapter.MaknaeCardId })
        {
            BscBookDialog.DoModal();
            yield return new WaitForSecondsRealtime(.8f);
            var book = UnityEngine.Object.FindObjectOfType<BscBookDialog>();
            if (book != null)
            {
                book.SendMessage("OnBookSelected", (uint)id, SendMessageOptions.RequireReceiver);
                book.SendMessage("OnWhereGet", SendMessageOptions.RequireReceiver);
            }
            checks["bookSourceBlocked" + id] = book != null && book.HasSelection && ShowsClosedStoryNotice();
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.6f);
        }

        var previews = LocalCardRegistry.MarketPreviews().Concat(LocalStoryChapter.MaknaeMarketPreviews()).ToArray();
        checks["shopCatalogueOmitsEveryStoryOffer"] = previews.All(row => LocalShopCatalog.Get(row.GoodsId) == null);
        BscShopGoldDialog.DoModalBoss();
        yield return new WaitForSecondsRealtime(2);
        var shop = UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();
        var shopUi = shop == null ? null : shop.GetComponent<LocalShopNativeUI>();
        for (int i = 0; i < 2; i++)
        {
            float limit = Time.realtimeSinceStartup + 8;
            while (shopUi != null && !shopUi.VisibleCells.Any(cell => cell.gameObject.activeInHierarchy) && Time.realtimeSinceStartup < limit)
                yield return new WaitForSecondsRealtime(.2f);
            checks["nativeShopShowsOrdinaryGoodsWithoutStoryOffers" + i] = shopUi != null &&
                shopUi.VisibleCells.Any(cell => cell.gameObject.activeInHierarchy) &&
                shopUi.VisibleCells.Where(cell => cell.gameObject.activeInHierarchy).All(cell => cell.StoryPreview == null &&
                    cell._lbCostCount?.text != "剧情获取");
            if (i == 0 && shop != null) shop.ToggleBossCurrency();
            yield return new WaitForSecondsRealtime(1);
        }

        var stale = new GameObject("OldStoryShopOffer").AddComponent<BscShopItem>();
        foreach (var row in previews)
        {
            stale._packageID = (uint)row.GoodsId;
            stale.SendMessage("OnBuyItem", SendMessageOptions.RequireReceiver);
            checks["staleShopShortcutBlocked" + row.GoodsId] = ShowsClosedStoryNotice() &&
                UnityEngine.Object.FindObjectOfType<BscShopBuyItemPopup>() == null && UnityEngine.Object.FindObjectOfType<LocalStoryMap>() == null;
            foreach (var dialog in UnityEngine.Object.FindObjectsOfType<NoticeDialog>()) dialog.On1();
            yield return new WaitForSecondsRealtime(.3f);
        }
        UnityEngine.Object.Destroy(stale.gameObject);
    }

    private IEnumerator CheckStoryChapters()
    {
        const string output = "-bnsm-competitive-output=";
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith(output, StringComparison.Ordinal)) folder = Path.GetFullPath(arg.Substring(output.Length));
        Directory.CreateDirectory(folder);
        var checks = new Dictionary<string, object>();
        float until = Time.realtimeSinceStartup + 90;
        while (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Game" && Time.realtimeSinceStartup < until)
            yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        yield return DismissStagePreviewNotices();
        Dialog.CloseAllSpace();
        if (Environment.GetCommandLineArgs().Contains("-bnsm-story-closed-test"))
        {
            yield return CheckClosedStory(checks);
            File.WriteAllText(Path.Combine(folder, "story-native.json"),
                LitJson.JsonMapper.ToJson(new { passed = errors == 0 && checks.Values.OfType<bool>().All(value => value), errors, checks }));
            Application.Quit();
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-bnsm-hong-growth-native"))
        {
            yield return CheckHongGrowthUi(checks);
            File.WriteAllText(Path.Combine(folder, "story-native.json"),
                LitJson.JsonMapper.ToJson(new { passed = errors == 0 && checks.Values.OfType<bool>().All(value => value), errors, checks }));
            Application.Quit();
            yield break;
        }
        var tables = Singleton<DataTable>.Instance;
        var player = MonoSingleton<MainClient>.Instance.Player;
        var card = tables.GetCardData(LocalHongCard.FirstCardId, false);
        var native = tables.GetCardData(LocalStoryChapter.MaknaeCardId, true);
        checks["nativeAndCustomResolveThroughRegistry"] = native != null && native.Id == LocalStoryChapter.MaknaeCardId &&
            card != null && card.Id == LocalHongCard.FirstCardId && tables.GetCardData(int.MaxValue, false) == null;
        checks["nativeAndCustomFragmentsResolveToTheirCard"] =
            tables.GetCardDataByPiece(LocalStoryChapter.MaknaeFragmentId)?.Id == LocalStoryChapter.MaknaeCardId &&
            tables.GetCardDataByPiece(LocalHongCard.FragmentId)?.Id == LocalHongCard.FirstCardId;
        var stats = card.GetBaseStatus(1, 0, false, false);
        int[] slots = { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        int[] expected = { 266, 56, 10, 10, 8, 16, 10, 4, 80, 12 };
        checks["levelOnePanel"] = slots.Select(i => (int)stats.stat[i].diff).SequenceEqual(expected);
        checks["actualPanel"] = slots.Select(i => (int)stats.stat[i].diff).ToArray();
        var bonds = tables.GetCardRelationData(card.Id);
        checks["nativeHongRelations"] = bonds.Count == 3 &&
            bonds.All(bond => bond.RankData.Count == 16 && bond.GetRankData(16).StatValueId.Contains(478)) &&
            bonds.All(bond => bond.CardId.All(id => tables.GetCardRelationData(id).Any(row => row.Id == bond.Id)));
        var gear = LocalHongEquipment.MarketPreviews().Select(offer => tables.GetItemData(offer.ProductId, false)).ToArray();
        checks["nativeHongEquipment"] = gear.Length == 3 &&
            gear.All(item => item != null && item.CardId == card.Id && item.Step == 1 && item.Next1Id != 0);
        var awake = tables.GetSkillData(LocalHongCard.FirstSkillId + 5, false);
        var passives = card.GetPassiveBuffStat(new List<UserCardSkill> { new UserCardSkill(awake, 2) });
        checks["stackedHit"] = passives.stat[6].diff == 120;
        checks["airFollowup"] = tables.GetSkillData(LocalHongCard.FirstSkillId + 3, false).GetSkillEffects()
            .Any(effect => effect.EventType == SEFFECT_EVENT_TYPE.SEFFECT_EVENT_ENEMY_AIR);
        var damage = tables.GetSkillEffectData(LocalHongCard.FirstEffectId, false);
        var dodge = tables.GetSkillEffectData(LocalHongCard.FirstEffectId + 2, false);
        checks["hongActiveDamageAndDodge"] = damage.GetValue(1, 99) == 190 && damage.GetValue(2, 1) == 266 &&
            (int)damage.GetValue(2, 99) == 3100 && dodge.GetValue(0, 99) == 500;
        var counter = tables.GetSkillEffectData(LocalHongCard.FirstEffectId + 9, false);
        var spirit = tables.GetSkillEffectData(LocalHongCard.FirstEffectId + 10, false);
        var healing = tables.GetSkillEffectData(LocalHongCard.DodgeSpiritHealEffectId, false);
        checks["hongNativeDodgeTriggers"] = counter.GetEventProb(99) == 20 && spirit.GetEventProb(99) == 20 &&
            counter.EventType == tables.GetSkillEffectData(14890, false).EventType &&
            spirit.EventType == tables.GetSkillEffectData(14822, false).EventType &&
            healing.EffectVisual == SEFFECT_VISUAL.SEFFECT_VISUAL_HEALING && healing.GetValue(0, 99) == 10;
        var healPrefab = AssetManager.Load("prefabs/battlefx/card/Effect_Skill_Common_Heal", typeof(GameObject)) as GameObject;
        checks["hongDodgeRecoveryPresentation"] = healPrefab != null &&
            healPrefab.GetComponentInChildren<ParticleSystem>(true) != null &&
            healPrefab.GetComponentsInChildren<SfxSpawn>(true).Any(x => x.playOnEnable && x.sfxCuePrefab != null);
        var maxCard = tables.GetCardData(LocalHongCard.LastCardId, false);
        var enchant = tables.GetSkillEnchantData(maxCard.SkillEnchantGroupId[0]);
        var multipliers = new[] { 211, 252, 313, 395, 498 };
        var fixedDamage = new[] { 3426, 3980, 4566, 4988, 5344 };
        checks["hongNativeEnchantTiers"] = enchant.SkillEnchantInfoList.Count == 5 && enchant.SkillEnchantInfoList.All(row =>
        {
            var skill = tables.GetSkillData(row.NextEnchantSkillId, false);
            var hit = tables.GetSkillEffectData(skill.SkillEffectId[0], false);
            var evade = tables.GetSkillEffectData(skill.CasterSkillEffectId[0], false);
            int tier = row.EnchantLevel;
            return skill.EnchantLevel == tier && hit.GetValue(1, 99) == multipliers[tier - 1] &&
                Mathf.Approximately(hit.GetValue(2, 99), fixedDamage[tier - 1]) && evade.GetValue(0, 99) == 500 + 50 * tier;
        });
        var trans = tables.GetTranscendenceData(maxCard.Id);
        var originalTrans = tables.GetTranscendenceData(LocalHongCard.TranscendenceTemplateId);
        checks["hongNativeTranscendence"] = trans.CardId == maxCard.Id && trans.GrowthItemId == LocalHongCard.FragmentId &&
            trans.EffectDataList.Select(x => x.Id).SequenceEqual(originalTrans.EffectDataList.Select(x => x.Id));
        checks["hongSixEnchantTreesHaveBattleEffects"] = Enumerable.Range(0, LocalHongCard.EnchantSlots).All(slot =>
        {
            var group = tables.GetSkillEnchantData(maxCard.SkillEnchantGroupId[slot]);
            int previous = LocalHongCard.FirstSkillId + slot;
            if (group?.SkillEnchantInfoList.Count != 5) return false;
            foreach (var row in group.SkillEnchantInfoList)
            {
                var before = tables.GetSkillData(row.PrevEnchantSkillId, false);
                var after = tables.GetSkillData(row.NextEnchantSkillId, false);
                if (row.PrevEnchantSkillId != previous || !row.MaterialItemId.Contains(LocalHongCard.FragmentId) ||
                    after?.EnchantLevel != row.EnchantLevel || before == null ||
                    after.SkillEffectId.SequenceEqual(before.SkillEffectId) ||
                    after.GetSkillEffects().Any(effect => effect == null || effect.EventSkillId > 0 &&
                        tables.GetSkillData(effect.EventSkillId, false)?.GetSkillEffects().Any(nested => nested == null) != false))
                    return false;
                previous = row.NextEnchantSkillId;
            }
            return true;
        });
        var minimi = AssetManager.Load(Global.minimiPrefabPath + card.Minimi, typeof(GameObject)) as GameObject;
        var moves = minimi == null ? null : minimi.GetComponent<Minimi>().skillAnimatorGroup.animatorKungfu.Asset;
        checks["yanhuangClipsOnHongRig"] = moves is AnimatorOverrideController &&
            new[] { "ANI_KungfuSinN_SkillA", "ANI_KungfuRanS_SkillA", "ANI_Kungfu_TriggerA" }.All(
                name => moves.animationClips.Any(clip => clip.name == name));

        foreach (int rank in new[] { 1, 5, 9 })
        {
            var row = tables.GetCardData(LocalHongCard.FirstCardId + rank - 1, false);
            BscFullCard.DoModal(row, (uint)rank, 1, 0, 0);
            yield return new WaitForSecondsRealtime(1.2f);
            var viewer = UnityEngine.Object.FindObjectOfType<BscFullCard>();
            checks["portrait" + rank] = viewer != null && viewer.coTexture.isVisible &&
                viewer.coTexture.mainTexture?.name == row.CardBig + "Face" &&
                viewer.coTexture.material.shader.name == "Local/HongPortraitRain" &&
                viewer.coLabelIllustrator.text == "By 骤雨";
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-rank-" + rank + ".png"));
            yield return new WaitForSecondsRealtime(.4f);
            if (rank == 1)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-rank-1-rain.png"));
                yield return new WaitForSecondsRealtime(.25f);
            }
            if (viewer != null) viewer.DoHide();
            yield return new WaitForSecondsRealtime(.5f);
        }

        var registered = LocalCardRegistry.FindExtensionForCard(card.Id);
        var bookEntry = player.GetCardBook(false).SingleOrDefault(x => x.Id == card.Id);
        checks["registeredBookBeforeUnlock"] = bookEntry != null && !bookEntry.IsCollected;
        BscBookDialog.DoModal();
        yield return new WaitForSecondsRealtime(.8f);
        var book = UnityEngine.Object.FindObjectOfType<BscBookDialog>();
        if (book != null) book.SendMessage("OnBookSelected", (uint)card.Id, SendMessageOptions.RequireReceiver);
        checks["nativeBookSelectsRegisteredCard"] = book != null && book.HasSelection && book.coLabelName.text.Contains(LocalHongCard.Name);
        if (book != null) book.SendMessage("OnWhereGet", SendMessageOptions.RequireReceiver);
        yield return new WaitForSecondsRealtime(.5f);
        checks["bookShowsStorySource"] = UnityEngine.Object.FindObjectsOfType<NoticeDialog>()
            .Any(x => !x.GonnaHide && x.lbNotice.text.Contains(registered.Chapter.Goal));
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.5f);

        BscShopGoldDialog.DoModalBoss();
        yield return new WaitForSecondsRealtime(1.5f);
        var shop = UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();
        var shopUi = shop == null ? null : shop.GetComponent<LocalShopNativeUI>();
        if (shopUi != null)
        {
            shopUi.SearchInput.text = LocalHongCard.Name;
            shopUi.SearchNow();
            yield return new WaitForSecondsRealtime(1.3f);
            var cells = shopUi.VisibleCells.Where(x => x.gameObject.activeInHierarchy && x.StoryPreview == registered).ToArray();
            checks["nativeMarketShowsCardAndFragment"] = cells.Length == 2 && cells.All(x => x._lbCostCount.text == "剧情获取");
            var fragment = tables.GetItemData(registered.FragmentId, false);
            checks["fragmentUsesRegisteredPortrait"] = fragment.Icon == card.CardMini && fragment.Atlas == card.CardMiniAtlas &&
                cells.SelectMany(x => x.GetComponentsInChildren<CardFrame>()).Any(x => x.mStyle == CardFrame.STYLE.ThumbNail_Item &&
                    x.coSpriteIllust != null && x.coSpriteIllust.spriteName == fragment.Icon &&
                    x.coSpriteIllust.atlas?.spriteMaterial?.mainTexture?.name == card.CardMini);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-boss-market.png"));
            yield return new WaitForSecondsRealtime(.4f);
            if (cells.Length != 0) cells[0].SendMessage("OnBuyItem", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(.5f);
            checks["nativeMarketOpensStorySource"] = UnityEngine.Object.FindObjectsOfType<NoticeDialog>()
                .Any(x => !x.GonnaHide && x.lbNotice.text.Contains(registered.Chapter.Goal)) &&
                UnityEngine.Object.FindObjectOfType<BscShopBuyItemPopup>() == null;
        }
        else checks["nativeMarketShowsCardAndFragment"] = false;
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.6f);

        var clear = player.GetClearStageID(GAME_DIFFICULTY.DIFFICULTY_NORMAL);
        checks["initiallyUnowned"] = !player.cards.Any(c => c?.cardData != null && LocalHongCard.IsCard(c.cardData.Id));
        var town = UnityEngine.Object.FindObjectOfType<Town>();
        var button = town?.RotationRoot?.GetComponentsInChildren<TownBuilding>(true).FirstOrDefault(x => x.name == "LocalStory");
        checks["townStoryEntry"] = button != null && button.menuName != null && button.menuName.text == "江湖篇章";
        File.WriteAllText(Path.Combine(folder, "story-progress.json"), LitJson.JsonMapper.ToJson(checks));
        if (button != null) button.gameObject.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
        else MainController.GetGameSceneController().SendMessage("OnLocalStory", SendMessageOptions.RequireReceiver);
        yield return new WaitForSecondsRealtime(.8f);
        var map = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
        checks["storyMenu"] = map != null && map.FirstStage == 8601;
        var manager = GameObject.FindGameObjectWithTag("PageRoot").GetComponent<DialogManager>();
        checks["storyUsesNativePageStack"] = map != null && map.Type == Dialog.DialogType.PAGE &&
            map.transform.parent == manager.BaseSpace(Dialog.DialogType.PAGE, map.SpaceID).transform;
        checks["storyPageFillsViewport"] = StoryPageFillsViewport(map);
        if (!Environment.GetCommandLineArgs().Contains("-bnsm-maknae-native"))
        {
            int width = Screen.width, height = Screen.height;
            Screen.SetResolution(960, 720, false);
            yield return new WaitForSecondsRealtime(.45f);
            checks["storyPageFillsFourByThree"] = Screen.width == 960 && Screen.height == 720 && StoryPageFillsViewport(map) &&
                StoryMapReceivesInput(StoryMapTarget(map, "ChapterToggle/Hit")) &&
                StoryMapReceivesInput(StoryMapTarget(map, "CloseMap/Hit"));
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "story-page-4by3.png"));
            yield return new WaitForSecondsRealtime(.3f);
            Screen.SetResolution(1536, 640, false);
            yield return new WaitForSecondsRealtime(.45f);
            checks["storyPageFillsUltrawide"] = Screen.width == 1536 && Screen.height == 640 && StoryPageFillsViewport(map) &&
                StoryMapReceivesInput(StoryMapTarget(map, "ChapterToggle/Hit")) &&
                StoryMapReceivesInput(StoryMapTarget(map, "CloseMap/Hit"));
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "story-page-wide.png"));
            yield return new WaitForSecondsRealtime(.3f);
            Screen.SetResolution(width, height, false);
            yield return new WaitForSecondsRealtime(.45f);
        }
        bool closed = ClickStoryMap(map, "CloseMap/Hit");
        yield return new WaitForSecondsRealtime(.3f);
        checks["storyBackReturnsToTown"] = closed && UnityEngine.Object.FindObjectOfType<LocalStoryMap>() == null &&
            button != null && button.gameObject.activeInHierarchy;
        if (button != null) button.gameObject.SendMessage("OnClick", SendMessageOptions.RequireReceiver);
        yield return new WaitForSecondsRealtime(.55f);
        map = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
        checks["storyDrawerOpens"] = ClickStoryMap(map, "ChapterToggle/Hit");
        yield return new WaitForSecondsRealtime(.25f);
        checks["storyDrawerListsChapters"] = StoryMapTarget(map, "StoryChapter8601/Hit") != null &&
            StoryMapTarget(map, "StoryChapter8611/Hit") != null;
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "story-library.png"));
        yield return new WaitForSecondsRealtime(.35f);
        checks["storyDrawerSwitchesChapters"] = ClickStoryMap(map, "StoryChapter8611/Hit") && map.FirstStage == 8611;
        yield return new WaitForSecondsRealtime(.3f);
        if (map != null)
        {
            var route = map.transform.Find("StoryLayout/MapWindow/Route");
            var nodes = route == null ? Array.Empty<Transform>() : Enumerable.Range(0, route.childCount)
                .Select(route.GetChild).Where(node => node.name.StartsWith("StoryStage")).ToArray();
            checks["nativeMaknaeMap"] = nodes.Length == 9 &&
                (Environment.GetCommandLineArgs().Contains("-bnsm-maknae-native") ||
                    nodes.Skip(1).All(node => node.Find("StageCaption/StageStatus").GetComponent<UILabel>().text.Contains("未解锁"))) &&
                nodes.Count(node => node.Find("StageCaption/StageStatus").GetComponent<UILabel>().text.Contains("魔王")) == 3;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "maknae-map.png"));
            yield return new WaitForSecondsRealtime(.35f);
            var pan = StoryMapTarget(map, "MapWindow/PanSurface");
            if (pan != null && nodes.Length == 9)
            {
                var lastHit = nodes[8].Find("StageHit");
                float startX = route.localPosition.x;
                checks["storyOffscreenNodeClipped"] = !StoryMapReceivesInput(lastHit);
                checks["storyPanSurfaceReceivesInput"] = StoryMapReceivesInput(pan);
                var firstHit = nodes.Select(node => node.Find("StageHit")).FirstOrDefault(StoryMapReceivesInput) ?? pan;
                var camera = UICamera.FindCameraForLayer(pan.gameObject.layer);
                var previousTouch = UICamera.currentTouch;
                var previousCamera = UICamera.currentCamera;
                var simulatedTouch = new UICamera.MouseOrTouch { current = firstHit.gameObject, pos = camera.cachedCamera.WorldToScreenPoint(firstHit.position) };
                UICamera.currentTouch = simulatedTouch;
                UICamera.currentCamera = camera.cachedCamera;
                try
                {
                    camera.ProcessTouch(true, false);
                    simulatedTouch.current = pan.gameObject;
                    simulatedTouch.delta = new Vector2(-1600, 0);
                    camera.ProcessTouch(false, false);
                    simulatedTouch.delta = Vector2.zero;
                    camera.ProcessTouch(false, true);
                }
                finally
                {
                    UICamera.currentTouch = previousTouch;
                    UICamera.currentCamera = previousCamera;
                }
                yield return new WaitForSecondsRealtime(.3f);
                checks["storyDragRevealsLastStage"] = route.localPosition.x < startX && StoryMapReceivesInput(lastHit) && !map.GonnaHide;
                var end = route.localPosition.x;
                pan.gameObject.SendMessage("OnDrag", new Vector2(-1600, 0), SendMessageOptions.RequireReceiver);
                checks["storyDragClamps"] = Mathf.Approximately(end, route.localPosition.x);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "maknae-map-end.png"));
                yield return new WaitForSecondsRealtime(.35f);
                if (!Environment.GetCommandLineArgs().Contains("-bnsm-maknae-native"))
                {
                    ClickStoryMap(map, "StoryStage8619/StageHit");
                    yield return new WaitForSecondsRealtime(.35f);
                    var locked = UnityEngine.Object.FindObjectsOfType<NoticeDialog>().FirstOrDefault(x => !x.GonnaHide &&
                        x.lbNotice.text.Contains("请先通关"));
                    checks["storyLockedStageReason"] = locked != null;
                    locked?.OnClose();
                    yield return new WaitForSecondsRealtime(.35f);
                }
            }
            ClickStoryMap(map, "ChapterToggle/Hit");
            yield return new WaitForSecondsRealtime(.3f);
            ClickStoryMap(map, "StoryLineWeapon/Hit");
            yield return new WaitForSecondsRealtime(.3f);
            checks["storyWeaponBranchSelectable"] = StoryMapTarget(map, "StoryChapter8604/Hit") != null;
            ClickStoryMap(map, "StoryLineOutfit/Hit");
            yield return new WaitForSecondsRealtime(.3f);
            checks["storyOutfitBranchSelectable"] = StoryMapTarget(map, "StoryChapter8603/Hit") != null;
            ClickStoryMap(map, "StoryLineCharacter/Hit");
            yield return new WaitForSecondsRealtime(.3f);
            ClickStoryMap(map, "StoryChapter8601/Hit");
            yield return new WaitForSecondsRealtime(.3f);
            checks["storyShortChapterFits"] = map.FirstStage == 8601 && StoryMapTarget(map, "NextStages/Hit") == null;
            ClickStoryMap(map, "StoryStage8601/StageHit");
        }
        yield return new WaitForSecondsRealtime(.8f);
        var notice = UnityEngine.Object.FindObjectsOfType<NoticeDialog>().FirstOrDefault(x => !x.GonnaHide);
        checks["storyDialogue"] = notice != null && notice.lbNotice.text.Contains("陌生老人");
        checks["storyDialogueKeepsMapBehindIt"] = map != null && map.isActiveAndEnabled;
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "story-dialogue.png"));
        yield return new WaitForSecondsRealtime(.35f);
        notice?.OnClose();
        yield return new WaitForSecondsRealtime(.6f);
        map = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
        checks["storyDialogueCloseReturnsToStage"] = map != null && map.FocusStage == 8601 &&
            ClickStoryMap(map, "StoryStage8601/StageHit");
        yield return new WaitForSecondsRealtime(.4f);
        notice = UnityEngine.Object.FindObjectsOfType<NoticeDialog>().FirstOrDefault(x => !x.GonnaHide);
        if (notice != null) notice.On1();
        yield return new WaitForSecondsRealtime(.8f);
        notice = UnityEngine.Object.FindObjectsOfType<NoticeDialog>().FirstOrDefault(x => !x.GonnaHide);
        checks["storyReveal"] = notice != null && notice.lbNotice.text.Contains("才知道他是洪玄公");
        if (notice != null) notice.On1();
        yield return new WaitForSecondsRealtime(1);
        var entry = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>();
        checks["nativeStoryPreview"] = entry != null;
        checks["storyDoesNotPromiseSweeps"] = entry != null && !entry.coEnterStageNormal.goSkipButton1.activeInHierarchy &&
            entry.coEnterStageNormal.goSkipTipLabel.text.Contains("进入战斗");
        if (entry != null)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "wuri-peak-preview.png"));
            yield return new WaitForSecondsRealtime(.4f);
            entry.coEnterStageNormal.SendMessage("OnEnterDeck", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(1);
            var deck = UnityEngine.Object.FindObjectOfType<BscDeckDialog>();
            checks["nativeStoryDeck"] = deck != null;
            if (deck != null)
            {
                deck.OnNext();
                yield return ExitProbeWaitBattle(checks, "hongChapter", GAME_MODE.MODE_NORMAL);
                if (exitProbeBattle != null)
                {
                    yield return new WaitForSecondsRealtime(1);
                    var battleCard = Enumerable.Range(0, 5).Select(i => exitProbeBattle.UiManager.Card.GetCardDeckPos((uint)i, false))
                        .FirstOrDefault(x => x?.cardImage != null && x.cardImageFull != null);
                    if (battleCard != null)
                    {
                        battleCard.SetCardImage(card, null, 0);
                        yield return global::LocalFrame.EndOfFrame();
                        checks["hongBattleFramedPortrait"] = FilledPortrait(battleCard.cardImage, LocalRegisteredCards.PortraitFrame);
                        checks["hongBattlePopoutProportions"] = FittedPortrait(battleCard.cardImageFull, LocalRegisteredCards.PortraitFrame) &&
                            battleCard.cardImageFull.uvRect.height <= .48f && battleCard.cardImageFull.uvRect.yMax > .99f;
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-battle-portrait.png"));
                        yield return new WaitForSecondsRealtime(.4f);
                    }
                    else checks["hongBattleFramedPortrait"] = false;
                    exitProbeBattle.IsAuto = true;
                    TimeManager.SetBattleScale(6);
                    until = Time.realtimeSinceStartup + 45;
                    UiBattleResultWin victory = null;
                    while ((victory = UnityEngine.Object.FindObjectOfType<UiBattleResultWin>()) == null && Time.realtimeSinceStartup < until)
                        yield return new WaitForSecondsRealtime(.2f);
                    TimeManager.SetBattleScale(1);
                    checks["hongChapterVictory"] = victory != null;
                    if (victory != null)
                    {
                        yield return new WaitForSecondsRealtime(2.5f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-recruitment.png"));
                        yield return new WaitForSecondsRealtime(.4f);
                        victory.SendMessage("OnExitBtn", SendMessageOptions.RequireReceiver);
                        yield return new WaitForSecondsRealtime(3);
                    }
                }
            }
        }
        checks["storyUnlockedCard"] = player.cards.Any(c => c?.cardData?.Id == LocalHongCard.FirstCardId);
        checks["bookMarksCollectedAfterStory"] = player.GetCardBook(false).SingleOrDefault(x => x.Id == card.Id)?.IsCollected == true;
        Dialog.CloseAllSpace();
        var recruited = player.cards.SingleOrDefault(x => x?.cardData?.Id == card.Id);
        BscMyCardDialog.DoModal();
        yield return new WaitForSecondsRealtime(.8f);
        var collection = UnityEngine.Object.FindObjectOfType<BscMyCardDialog>();
        if (collection != null && recruited != null) collection.FocusCard(recruited.cardSeq);
        yield return new WaitForSecondsRealtime(1);
        var cell = collection == null ? null : collection.coFlowListView.GetComponentsInChildren<BscMyCardFlowListViewCell>()
            .FirstOrDefault(x => x.CardInfo?.cardSeq == recruited?.cardSeq);
        var frame = cell == null ? null : cell.coCardFrame.GetCom<CardFrame>();
        var selected = frame?.SelectedPortrait;
        checks["hongSelectedBreaksFrame"] = collection != null && !collection.coTextureMain.enabled &&
            selected != null && selected.gameObject.activeInHierarchy && selected.isVisible && frame.FramedPortrait.alpha == 0f &&
            selected.panel != frame.coSpriteFrame.panel &&
            selected.PortraitBounds is Rect breakout && breakout.yMax > 1f && breakout.xMin < 0f && breakout.xMax > 1f &&
            FittedPortrait(selected, LocalRegisteredCards.SelectedPortraitArea);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-card-selected.png"));
        yield return new WaitForSecondsRealtime(.4f);
        checks["ownedCardUsesNativeFace"] = frame != null && frame.mStyle == CardFrame.STYLE.Small &&
            frame.FramedPortrait?.mainTexture?.name == card.CardSmall;
        if (frame != null)
        {
            collection.coFlowListView.Press(true);
            collection.coFlowListView.OnDrag(new Vector2(-40f, 0f));
            yield return new WaitForSecondsRealtime(.15f);
            yield return global::LocalFrame.EndOfFrame();
            checks["framedPortraitStaysInFrame"] = (selected == null || !selected.gameObject.activeSelf) && frame.FramedPortrait.alpha == 1f &&
                frame.FramedPortrait.transform.parent == frame.coIllust.transform &&
                FilledPortrait(frame.FramedPortrait, LocalRegisteredCards.PortraitFrame);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-card-unselected.png"));
            yield return new WaitForSecondsRealtime(.4f);
            collection.coFlowListView.Press(false);
            foreach (int rank in new[] { 1, 5, 9 })
            {
                var row = tables.GetCardData(card.Id + rank - 1, false);
                frame.SetFrame(row, 0u, 0u, true, false, 0, false, 0, 0, 0);
                yield return global::LocalFrame.EndOfFrame();
                var image = frame.FramedPortrait;
                checks["nativeCardFace" + rank] = image.mainTexture?.name == row.CardSmall &&
                    FilledPortrait(image, LocalRegisteredCards.PortraitFrame);
            }
        }
        Dialog.CloseAllSpace();
        yield return new WaitForSecondsRealtime(.5f);
        checks["hongDetailFillsPanel"] = checks["hongDeckFillsCard"] = false;
        if (recruited != null)
        {
            BscCardInfoDialog.DoModal(BscCardInfoDialog.Style.Skill, recruited.cardSeq, new List<ulong> { recruited.cardSeq },
                null, null, false, false, true, true);
            yield return new WaitForSecondsRealtime(1);
            var details = UnityEngine.Object.FindObjectOfType<BscCardInfoDialog>();
            checks["hongDetailFillsPanel"] = details != null &&
                FilledPortrait(details.coTextureMain, LocalRegisteredCards.PortraitDetails) &&
                details.coTextureMain.material.shader.name == "Local/HongCardForeground";
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-card-detail.png"));
            yield return new WaitForSecondsRealtime(.4f);
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.5f);
            LocalStoryMenu.Preview(LocalStoryChapter.Hong);
            yield return new WaitForSecondsRealtime(.8f);
            var preview = UnityEngine.Object.FindObjectOfType<BscEnterStageDialog>();
            if (preview != null)
            {
                preview.coEnterStageNormal.SendMessage("OnEnterDeck", SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(1);
                var deck = UnityEngine.Object.FindObjectOfType<BscDeckDialog>();
                var slot = deck?.coDeckCards.FirstOrDefault(x => x != null && x.gameObject.activeInHierarchy);
                if (slot != null)
                {
                    slot.UpdateInfo(recruited.cardSeq);
                    yield return global::LocalFrame.EndOfFrame();
                    var portrait = slot.coCardFrame.GetCom<CardFrame>().FramedPortrait;
                    checks["hongDeckFillsCard"] = portrait != null && portrait.isVisible &&
                        FilledPortrait(portrait, LocalRegisteredCards.PortraitFrame);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-deck-portrait.png"));
                    yield return new WaitForSecondsRealtime(.4f);
                }
            }
        }
        if (Environment.GetCommandLineArgs().Contains("-bnsm-maknae-native"))
        {
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.5f);
            foreach (int id in new[] { 8613, 8616, 8619 })
            {
                var boss = LocalStoryChapter.Find(id);
                LocalStoryMenu.Preview(boss);
                yield return new WaitForSecondsRealtime(.6f);
                var battlePreview = UnityEngine.Object.FindObjectsOfType<BscEnterStageDialog>().FirstOrDefault(x => !x.GonnaHide);
                if (battlePreview != null) battlePreview.coEnterStageNormal.SendMessage("OnEnterDeck", SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(.8f);
                var battleDeck = UnityEngine.Object.FindObjectsOfType<BscDeckDialog>().FirstOrDefault(x => !x.GonnaHide);
                if (battleDeck != null) battleDeck.OnNext();
                yield return ExitProbeWaitBattle(checks, "maknae" + id, GAME_MODE.MODE_NORMAL);
                if (exitProbeBattle == null) break;
                exitProbeBattle.IsAuto = true;
                TimeManager.SetBattleScale(6);
                until = Time.realtimeSinceStartup + 40;
                UiBattleResultWin victory = null;
                while ((victory = UnityEngine.Object.FindObjectOfType<UiBattleResultWin>()) == null &&
                    UnityEngine.Object.FindObjectOfType<UiBattleResultLose>() == null && Time.realtimeSinceStartup < until)
                    yield return new WaitForSecondsRealtime(.2f);
                TimeManager.SetBattleScale(1);
                checks["maknaeVictory" + id] = victory != null;
                if (victory == null) break;
                yield return new WaitForSecondsRealtime(1.4f);
                victory.SendMessage("OnExitBtn", SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(2.5f);
                var chapter = UnityEngine.Object.FindObjectOfType<LocalStoryMap>();
                checks["maknaeReturnsToChapter" + id] = chapter != null &&
                    chapter.FirstStage == LocalStoryChapter.MaknaeFirstStage && chapter.FocusStage == id;
                Dialog.CloseAllSpace();
                yield return new WaitForSecondsRealtime(.6f);
            }
            checks["maknaeNativeRecruitment"] = player.cards.Any(x => x?.cardData?.EvolveFirstAncestor == LocalStoryChapter.MaknaeCardId);
            var lastBoss = LocalStoryChapter.Find(8619);
            LocalStoryMenu.Preview(lastBoss);
            yield return new WaitForSecondsRealtime(.7f);
            var sweepPreview = UnityEngine.Object.FindObjectsOfType<BscEnterStageDialog>().FirstOrDefault(x => !x.GonnaHide);
            var sweepUi = sweepPreview?.coEnterStageNormal;
            checks["maknaeSweepButtonAvailable"] = sweepUi != null && sweepUi.goSkipButton1.activeInHierarchy &&
                sweepUi.goSkipTipLabel.text.Contains("3/3");
            if (sweepUi != null && sweepUi.goSkipButton1.activeInHierarchy)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "maknae-boss-preview.png"));
                yield return new WaitForSecondsRealtime(.4f);
                sweepUi.SendMessage("OnSkipBattle", 0, SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(2);
                checks["maknaeNativeSweepResult"] = UnityEngine.Object.FindObjectOfType<UiBattleResultSkip>() != null &&
                    LocalStoryMenu.Progress(8619)?.sweepsLeft == 2;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "maknae-sweep-result.png"));
                yield return new WaitForSecondsRealtime(.4f);
            }
        }
        checks["normalProgressPreserved"] = player.GetClearStageID(GAME_DIFFICULTY.DIFFICULTY_NORMAL) == clear;
        bool passed = errors == 0 && checks.Values.OfType<bool>().All(x => x);
        File.WriteAllText(Path.Combine(folder, "story-native.json"), LitJson.JsonMapper.ToJson(new { passed, errors, checks }));
        Application.Quit();
    }

    private IEnumerator CheckHongGrowthUi(Dictionary<string, object> checks)
    {
        var player = MonoSingleton<MainClient>.Instance.Player;
        var stages = new[] { 5, 8, 16 }.Select(rank => player.cards.Single(card =>
            card?.cardData?.Id == LocalHongCard.FirstCardId + rank - 1)).ToArray();
        var sequences = stages.Select(card => card.cardSeq).ToList();
        var max = stages[2];
        checks["hongTranscendenceUsesOriginalRankGate"] =
            Transcendence.GetTranscendenceEnableState(stages[1].cardSeq) == Transcendence.TranscendenceEnableState.UNABLE_NOT_ENOUGHT_EVOLVE &&
            Transcendence.GetTranscendenceEnableState(max.cardSeq) == Transcendence.TranscendenceEnableState.ENABLE;

        foreach (var card in new[] { stages[0], max })
        {
            BscCardInfoDialog.DoModal(BscCardInfoDialog.Style.Equip, card.cardSeq, sequences, null, null, false, false, true, true);
            yield return new WaitForSecondsRealtime(.8f);
            var details = UnityEngine.Object.FindObjectOfType<BscCardInfoDialog>();
            if (details == null) { checks["hongCardDetailExists"] = false; yield break; }
            var expected = card.cardData.Rank == 5 ? FRAME_TYPE.Purple : FRAME_TYPE.Orange;
            checks["hongDetailRankTint" + card.cardData.Rank] =
                details.coSpriteCover.isVisible && details.coSpriteCover.color == details.colorIllustCovers[(int)expected];
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-rank-tint-" + card.cardData.Rank + ".png"));
            yield return new WaitForSecondsRealtime(.35f);
            if (card != max)
            {
                Dialog.CloseAllSpace();
                yield return new WaitForSecondsRealtime(.45f);
                continue;
            }

            details.SendMessage("OnTransTabClick", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(.6f);
            checks["hongNativeTranscendencePage"] = details.coTrans.enableGroup.activeInHierarchy &&
                details.coTrans.ocSkillList.Count == 5;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-transcendence-page.png"));
            yield return new WaitForSecondsRealtime(.4f);
            details.coTrans.SendMessage("OnGrowthBtnClick", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(.6f);
            var growth = UnityEngine.Object.FindObjectOfType<TranscendencePopupDialog>();
            uint held = player.GetItemCount(LocalHongCard.FragmentId);
            growth?.SendMessage("OnGrowthBtnClick", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(2);
            checks["hongNativeGrowthConsumesOwnFragments"] = growth != null &&
                player.GetCard(max.cardSeq).effectDataInfoList.Count == 3 &&
                player.GetItemCount(LocalHongCard.FragmentId) == held - 10;
            if (growth != null)
            {
                growth.SendMessage("OnApplyBtnClick", SendMessageOptions.RequireReceiver);
                growth.SendMessage("OnClose", SendMessageOptions.RequireReceiver);
            }
            yield return new WaitForSecondsRealtime(.6f);
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.45f);

            BscCardInfoDialog.DoModal(BscCardInfoDialog.Style.Skill, max.cardSeq, sequences, null, null, false, false, true, true);
            yield return new WaitForSecondsRealtime(.65f);
            details = UnityEngine.Object.FindObjectOfType<BscCardInfoDialog>();
            var rules = player.GetCard(max.cardSeq).GetSkillEnchantInfoList();
            checks["hongNativeSixEnchantChoices"] = rules.Count == 6 &&
                rules.All(row => row.EnchantLevel == 1 && row.MaterialItemId.Contains(LocalHongCard.FragmentId));
            details?.coSkills.SendMessage("OnClickSkillEnchant", SendMessageOptions.RequireReceiver);
            yield return new WaitForSecondsRealtime(.65f);
            var popup = UnityEngine.Object.FindObjectOfType<BscSkillEnchantPopup>();
            checks["hongNativeEnchantPopupOpens"] = popup != null;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "hong-enchant-popup.png"));
            yield return new WaitForSecondsRealtime(.4f);
            Dialog.CloseAllSpace();
            yield return new WaitForSecondsRealtime(.45f);
            var client = MonoSingleton<ClientUpdate>.Instance;
            for (int slot = 0; slot < LocalHongCard.EnchantSlots; slot++)
            {
                bool settled = false, succeeded = false;
                client.DoRequestSkillEnchant((CardSkillType)slot, max.cardSeq,
                    reply => { succeeded = reply.result == CraftResult.CRAFT_RESULT_SUCCESS && reply.cardInfo.cardSkillEnchant[slot] == 1; settled = true; },
                    (_, _, _) => settled = true);
                float timeout = Time.realtimeSinceStartup + 8;
                while (!settled && Time.realtimeSinceStartup < timeout) yield return null;
                checks["hongNativeSkillUpgrade" + slot] = succeeded;
            }
        }
    }

    private static bool FilledPortrait(UITexture image, Rect area) =>
        image.PortraitBounds == area && image.uvRect.width < .90f && FittedPortrait(image, area);

    private static bool FittedPortrait(UITexture image, Rect area)
    {
        if (image.mainTexture == null || image.PortraitBounds is not Rect bounds) return false;
        const float tolerance = .005f;
        if (bounds.xMin < area.xMin - tolerance || bounds.yMin < area.yMin - tolerance ||
            bounds.xMax > area.xMax + tolerance || bounds.yMax > area.yMax + tolerance) return false;
        var scale = image.transform.lossyScale;
        var uv = image.uvRect;
        float renderedAspect = Mathf.Abs(bounds.width * scale.x / (bounds.height * scale.y));
        float sourceAspect = image.mainTexture.width * uv.width / (image.mainTexture.height * uv.height);
        return Mathf.Abs(renderedAspect - sourceAspect) < tolerance;
    }

}
