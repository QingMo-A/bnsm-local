using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LocalHongCardBuild
{
    private const string Root = "Assets/Resources/LocalHongXuangong/";
    private const string Clips = "Assets/AnimationClip/";

    public static void Prepare()
    {
        PreparePortrait("Default", new Rect(.26f, .043f, .30f, .21f));
        PreparePortrait("FiveStar", new Rect(.25f, .052f, .31f, .214f));
        PreparePortrait("Awakened", new Rect(.23f, .05f, .34f, .19f));
        PrepareModel();
        AssetDatabase.SaveAssets();
        Debug.Log("HONG_XUANGONG_ASSETS_PREPARED");
    }

    private static void PreparePortrait(string form, Rect face)
    {
        string art = LocalHongCard.AssetPrefix + form;
        string path = Root + art + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var full = Material(form + "DisplayMaterial", Shader.Find("Local/OriginalCardLive2D"), texture);
        var thumb = Material(form + "Material", Shader.Find("Unlit/Transparent Colored"), texture);
        var display = new GameObject(art);
        var atlasObject = new GameObject(art + "Atlas");
        try
        {
            var setting = display.AddComponent<Live2DModelSetting>();
            setting.modelFile = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/LocalOriginalCard/shenyan.moc.bytes");
            setting.layout = new Live2DModelSetting.Layout {
                width = 2.5f * texture.width / texture.height, height = 2.5f, center = Vector2.zero
            };
            setting.materials = new List<Material> { full };
            setting.motionGroupList = new List<Live2DModelSetting.MotionGroup>();
            setting.additionalBreath = new List<Live2DModelSetting.AdditionalBreath>();
            PrefabUtility.SaveAsPrefabAsset(display, Root + form + "Display.prefab");

            var atlas = atlasObject.AddComponent<UIAtlas>();
            atlas.spriteMaterial = thumb;
            var crop = new Rect(face.x * texture.width, face.y * texture.height, face.width * texture.width, face.height * texture.height);
            atlas.spriteList = new List<UIAtlas.Sprite> { new UIAtlas.Sprite { name = art, outer = crop, inner = crop } };
            PrefabUtility.SaveAsPrefabAsset(atlasObject, Root + form + "Atlas.prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(display);
            UnityEngine.Object.DestroyImmediate(atlasObject);
        }
    }

    private static Material Material(string name, Shader shader, Texture texture)
    {
        if (shader == null) throw new InvalidDataException("Hong Xuangong portrait shader unavailable");
        string path = Root + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.mainTexture = texture;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void PrepareModel()
    {
        const string model = "Assets/Resources/prefabs/minimi/3DCH9023.prefab";
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/Resources/animator/b_type/ANI_Kungfu_SkillB_controller.controller");
        var clipNames = new Dictionary<string, string> {
            ["ANI_KungfuSinN_SkillB"] = "ANI_KungfuSinN_SkillA",
            ["ANI_KungfuRanS_SkillB"] = "ANI_KungfuRanS_SkillA",
            ["ANI_Kungfu_TriggerB"] = "ANI_Kungfu_TriggerA"
        };
        string path = Root + "SkillActions.overrideController";
        var adapted = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (adapted == null)
        {
            adapted = new AnimatorOverrideController(controller);
            AssetDatabase.CreateAsset(adapted, path);
        }
        var replacements = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        var prefab = PrefabUtility.LoadPrefabContents(model);
        try
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            foreach (var source in controller.animationClips.Distinct())
            {
                if (!clipNames.TryGetValue(source.name, out string target)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + target + ".anim");
                if (clip == null) throw new InvalidDataException("Missing Yanhuang animation: " + target);
                int bound = AnimationUtility.GetCurveBindings(clip).Count(binding =>
                    string.IsNullOrEmpty(binding.path) || animator.transform.Find(binding.path) != null);
                if (bound == 0) throw new InvalidDataException("No Hong Xuangong bindings for " + target);
                Debug.Log($"HONG_XUANGONG_MOTION {target}: {bound} bound curves");
                replacements.Add(new KeyValuePair<AnimationClip, AnimationClip>(source, clip));
            }
            if (replacements.Count != clipNames.Count) throw new InvalidDataException("Missing original kungfu state clips");
            adapted.ApplyOverrides(replacements);
            EditorUtility.SetDirty(adapted);
            prefab.name = LocalHongCard.AssetPrefix + "Model";
            var minimi = prefab.GetComponent<Minimi>();
            minimi.skillAnimatorGroup.animatorKungfu.Asset = adapted;
            PrefabUtility.SaveAsPrefabAsset(prefab, Root + "model.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }
}
