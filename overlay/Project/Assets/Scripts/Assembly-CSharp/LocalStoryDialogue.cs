using System;
using System.Collections.Generic;
using System.Linq;
using BscTable;
using UnityEngine;
using static EpisodePlayerChapter.StepType;

internal static class LocalStoryDialogue
{
    [Serializable]
    internal sealed class Line
    {
        public string Speaker, Text;
        public MINIMI_MOTION_TYPE Motion;
    }

    [Serializable]
    private sealed class Scene
    {
        public int Stage;
        public string Art;
        public Line[] Before, After;
    }

    [Serializable]
    private sealed class Catalog
    {
        public Scene[] Scenes;
    }

    private static readonly Lazy<Dictionary<int, Scene>> Scenes = new Lazy<Dictionary<int, Scene>>(() =>
        JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("LocalStoryDialogue").text)
            .Scenes.ToDictionary(scene => scene.Stage));

    private static readonly Lazy<GameObject> Cues = new Lazy<GameObject>(() => Episode.LoadPrefab("LocalChapter53_Start"));
    private static readonly string[] PlayerSpeakers = { "主角", "助手" };

    private static Line[] Lines(int id, Episode.FileType type)
    {
        var chapter = LocalStoryChapter.Find(id);
        if (type == Episode.FileType.Start && chapter?.BattleOnly == true) return null;
        if (Scenes.Value.TryGetValue(id, out var scene))
            return type == Episode.FileType.End ? scene.After : scene.Before;
        if (type == Episode.FileType.Start && chapter != null)
            return new[] { new Line { Speaker = "主角", Text = chapter.Opening },
                new Line { Speaker = "助手", Text = chapter.Scene } };
        return null;
    }

    internal static bool HasScene(int id, Episode.FileType type) => Lines(id, type)?.Length > 0;

    internal static string Backdrop(int id)
    {
        if (Scenes.Value.TryGetValue(id, out var scene) && !string.IsNullOrEmpty(scene.Art)) return scene.Art;
        if (LocalAdventureChapter53.Find(id)?.HasDialogue == true) return "WhiteBirchForest";
        var chapter = LocalStoryChapter.Find(id);
        return chapter == null ? null : LocalStoryChapter.All.FirstOrDefault(entry => entry.StoryId == chapter.StoryId &&
            entry.Act == chapter.Act && !string.IsNullOrEmpty(entry.MapArt))?.MapArt ?? chapter.BattleArt;
    }

    internal static bool Load(EpisodePlayer player, int id, Episode.FileType type, string bgm)
    {
        var lines = Lines(id, type);
        if (lines == null || lines.Length == 0) return false;

        string title = LocalStoryChapter.Find(id)?.Title ?? LocalAdventureChapter53.Find(id)?.Title;
        if (title == null) return false;
        var root = new GameObject("LocalDialogue-" + id + "-" + type);
        root.SetActive(false);
        root.transform.SetParent(player.transform, false);
        var chapter = root.AddComponent<EpisodePlayerChapter>();
        chapter.chapterName = root.name;
        chapter.replayTitleAlias = title;
        int order = 0;
        T Step<T>(EpisodePlayerChapter.StepType kind) where T : EpisodePlayerData
        {
            var step = root.AddComponent<T>();
            step.stepType = kind;
            step._Order = order++;
            return step;
        }

        var setting = Step<EpisodePlayerDataEnterScene>(EnterScene);
        setting.waitTimeToEnd = .6f;
        if (type == Episode.FileType.Start && Cues.Value != null)
        {
            var titleStep = Step<EpisodePlayerDataTitleScene>(TitleScene);
            titleStep.titleEffectPrefab = Cues.Value.GetComponent<EpisodePlayerDataTitleScene>().titleEffectPrefab;
            titleStep.textAlias = title;
            titleStep.waitTimeToEnd = 1.4f;
        }
        var actors = Step<EpisodePlayerDataEnterActor>(EnterActor);
        actors.waitTimeToEnd = .7f;
        actors.actors.Add(new EpisodePlayerData.ActorInfo
        {
            actorType = EpisodePlayerData.ActorType.Player,
            enterPosition = EpisodePlayerData.ActorPosType.Left_1,
            motion = MINIMI_MOTION_TYPE.MINIMI_MOTION_ENTER
        });
        int position = (int)EpisodePlayerData.ActorPosType.Right_1;
        foreach (string speaker in lines.Select(line => line.Speaker).Distinct().Except(PlayerSpeakers))
        {
            setting.npcInfos.Add(Actor(speaker));
            actors.actors.Add(new EpisodePlayerData.ActorInfo { actorType = EpisodePlayerData.ActorType.NPC,
                npcNickname = speaker, enterPosition = (EpisodePlayerData.ActorPosType)position++ });
        }
        var cue = Cues.Value?.GetComponent<EpisodePlayerDataSound>()?.sfxCue;
        if (cue != null)
        {
            var sound = Step<EpisodePlayerDataSound>(Sound);
            sound.sfxCue = cue;
            sound.waitTimeToEnd = .3f;
        }
        foreach (var line in lines)
        {
            var dialogue = Step<EpisodePlayerDataDialog>(EpisodePlayerChapter.StepType.Dialog);
            dialogue.actorType = line.Speaker == "主角" ? EpisodePlayerData.ActorType.Player :
                line.Speaker == "助手" ? EpisodePlayerData.ActorType.Helper : EpisodePlayerData.ActorType.NPC;
            dialogue.npcNickname = line.Speaker;
            dialogue.textAlias = line.Text;
            dialogue.localMotion = line.Motion;
        }
        var exit = Step<EpisodePlayerDataExitScene>(ExitScene);
        exit.waitTimeToEnd = .6f;
        root.SetActive(true);
        player.AddChapter(chapter, string.IsNullOrEmpty(bgm) ? "EP_Drama" : bgm);
        return true;
    }

    private static EpisodePlayerDataEnterScene.NpcInfo Actor(string speaker)
    {
        var npc = new EpisodePlayerDataEnterScene.NpcInfo { name = speaker, pos = new Vector3(-40, -75, 0) };
        switch (speaker)
        {
            case "老小": npc.id = 2511030; break;
            case "法器匠": npc.id = 2511079; break;
            case "押运首领": npc.id = 2511073; break;
            case "老人":
            case "洪玄公":
                npc.modelResource = "LocalHongXuangong/model";
                npc.displayName = speaker;
                break;
            default: throw new InvalidOperationException("Unknown local episode speaker " + speaker);
        }
        return npc;
    }
}
