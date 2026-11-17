using System;
using System.Collections.Generic;
using BscTable;
using UnityEngine;

internal static class LocalRegisteredCards
{
    private static readonly Dictionary<int, SkillData> Skills = new Dictionary<int, SkillData>();
    private static readonly Dictionary<int, SkillEffectData> Effects = new Dictionary<int, SkillEffectData>();
    private static readonly Dictionary<int, ItemData> Fragments = new Dictionary<int, ItemData>();
    private static readonly Dictionary<int, PieceData> Pieces = new Dictionary<int, PieceData>();

    private static Dictionary<int, ItemData> equipment;

    internal static ItemData Equipment(int id, Func<int, ItemData> original)
    {
        if (equipment == null)
        {
            var rows = new Dictionary<int, ItemData>();
            foreach (var row in LocalCardRegistry.Equipment(original)) rows.Add(row.Id, row);
            equipment = rows;
        }
        return equipment[id];
    }

    internal static void Clear()
    {
        Skills.Clear();
        Effects.Clear();
        Fragments.Clear();
        Pieces.Clear();
        equipment = null;
    }

    internal static void InitializeGeneratedCard(CardData card, LocalCardRegistry.Entry entry)
    {
        if (entry == null) return;
        card.Minimi = entry.ArtPrefix + "Model";
        card.UpdateModifyStatus();
    }

    internal static SkillData Skill(int id, bool constCache, Func<int, bool, SkillData> original, Func<int, CardData> originalCard)
    {
        var entry = LocalCardRegistry.FindSkill(id);
        bool prototype = LocalOriginalCardDefinition.IsSkill(id);
        if (entry == null && !prototype) return original(id, constCache);
        if (!Skills.TryGetValue(id, out var skill))
        {
            skill = prototype
                ? LocalCardRegistry.CreatePrototypeSkill(id, originalCard, template => original(template, true))
                : entry.CreateSkill(id, template => original(template, true), originalCard);
            Skills.Add(id, skill);
        }
        return skill;
    }

    internal static SkillEffectData Effect(LocalCardRegistry.Entry entry, int id, Func<int, SkillEffectData> original)
    {
        if (!Effects.TryGetValue(id, out var effect))
        {
            effect = entry.CreateEffect(id, original);
            effect.Update();
            Effects.Add(id, effect);
        }
        return effect;
    }

    internal static ItemData Fragment(LocalCardRegistry.Entry entry, Func<int, ItemData> original)
    {
        if (!Fragments.TryGetValue(entry.FragmentId, out var item))
        {
            item = entry.CreateFragment(original(entry.FragmentTemplateId));
            Fragments.Add(entry.FragmentId, item);
        }
        return item;
    }

    internal static PieceData Piece(LocalCardRegistry.Entry entry, Func<int, PieceData> original)
    {
        if (!Pieces.TryGetValue(entry.FragmentId, out var piece))
        {
            piece = entry.CreatePiece(original(entry.FragmentTemplateId).Complete);
            Pieces.Add(entry.FragmentId, piece);
        }
        return piece;
    }

    internal static readonly Rect PortraitFrame = new Rect(0f, 0f, 1f, 1f);
    internal static readonly Rect PortraitDetails = new Rect(.12f, .01f, .76f, .90f);

    internal static string CardFacePath(CardData card, int skinId)
    {
        if (!LocalMode.Enabled || card == null || skinId != 0 || LocalCardRegistry.FindExtensionForCard(card.Id) is not LocalCardRegistry.Entry entry)
            return null;
        return entry.ArtPrefix + "/" + card.CardSmall;
    }

    internal static readonly Rect SelectedPortraitArea = new Rect(-.60f, 0f, 2.20f, 1.09f);

    internal static bool ApplyPortrait(UITexture widget, CardData card, int skinId, Rect area, bool fullBody = false,
        bool selected = false, bool softEdges = false, bool battleAttack = false)
    {
        if (widget == null) return false;
        string path = CardFacePath(card, skinId);
        var texture = path == null ? null : AssetManager.Load(path, typeof(Texture)) as Texture;
        if (texture == null)
        {
            widget.ClearPortraitFit();
            return false;
        }
        Rect crop = PortraitFrame;
        if (!fullBody)
        {
            var entry = LocalCardRegistry.FindExtensionForCard(card.Id);
            float height = Mathf.Clamp(entry.PortraitHeight, .1f, 1f);
            // Fit one source composition to each widget without changing its crop.
            if (battleAttack) height = Mathf.Min(height, .48f);
            float width = Mathf.Min(1f, height * (selected ? .99f : battleAttack ? 1.12f : 1.30f) * texture.height / texture.width);
            crop = new Rect(Mathf.Clamp(entry.PortraitCenterX - width / 2f, 0f, 1f - width), 1f - height, width, height);
        }
        widget.dynamicMainTextureName = path;
        widget.mainTexture = texture;
        widget.uvRect = crop;
        float sourceAspect = texture.width * crop.width / (texture.height * crop.height);
        if (selected || fullBody || battleAttack)
        {
            widget.FitPortrait(area, sourceAspect, 0f);
            widget.SetPortraitFade(selected);
        }
        else
        {
            widget.CoverPortrait(area, crop, sourceAspect, softEdges);
        }
        return true;
    }

    internal static bool IsRegisteredPortrait(string textureName)
    {
        foreach (var entry in LocalCardRegistry.Entries)
            if (textureName.StartsWith(entry.ArtPrefix, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static CardLayout Layout(CardData card) => new CardLayout
    {
        CardName = card.CardSmall,
        CropTilling_X = 1f, CropTilling_Y = 1f, CropOffset_X = 0f, CropOffset_Y = 0f,
        ListTilling_X = 1f, ListTilling_Y = 1f, ListOffset_X = 0f, ListOffset_Y = 0f,
        NonCropPos_X = 0f, NonCropPos_Y = 0f
    };

    internal static UnityEngine.Object LoadAsset(string path, Type type)
    {
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        string key = normalized.Substring(slash + 1);
        foreach (var entry in LocalCardRegistry.Entries)
        {
            if (!key.StartsWith(entry.ArtPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (normalized.StartsWith(entry.ArtPrefix + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return LoadRegisteredAsset(entry, normalized.ToLowerInvariant(), key.Substring(entry.ArtPrefix.Length), type);
        }
        return null;
    }

    private static UnityEngine.Object LoadRegisteredAsset(LocalCardRegistry.Entry entry, string path, string key, Type type)
    {
        string root = entry.ArtPrefix + "/";
        if (path.Contains("/minimi/"))
            return AsType(Resources.Load<GameObject>(root + "model"), type);
        string form = key.StartsWith("Awakened", StringComparison.OrdinalIgnoreCase) ? "Awakened"
            : key.StartsWith("FiveStar", StringComparison.OrdinalIgnoreCase) ? "FiveStar" : "Default";
        string resource = null;
        if (path.Contains("/thumb/materials/")) resource = form + "Material";
        else if (path.Contains("/thumb/")) resource = typeof(Texture).IsAssignableFrom(type) ? entry.ArtPrefix + form : form + "Atlas";
        else if (path.Contains("/crop/")) resource = entry.ArtPrefix + form;
        else if (path.Contains("/detail/") || path.Contains("/spcard/") || path.Contains("/mcsource/prefabs/")) resource = form + "Display";
        return resource == null ? null : AsType(Resources.Load(root + resource), type);
    }

    private static UnityEngine.Object AsType(UnityEngine.Object asset, Type type)
    {
        if (asset == null || type.IsInstanceOfType(asset)) return asset;
        return asset is GameObject prefab && typeof(Component).IsAssignableFrom(type) ? prefab.GetComponent(type) : null;
    }
}
