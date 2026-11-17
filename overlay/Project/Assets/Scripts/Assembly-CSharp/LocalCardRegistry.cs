#if BNSM_SERVER
extern alias game;
using game::BscTable;
#else
using BscTable;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class LocalCardRegistry
{
    // The shared release catalogue. Prototypes have their own opt-in path.
    internal static readonly IReadOnlyList<Entry> Entries = Array.AsReadOnly(new[]
    {
        new Entry(LocalHongCard.FirstCardId, LocalHongCard.LastCardId, LocalHongCard.CollectionId,
            LocalHongCard.FragmentId, LocalHongCard.FragmentTemplateId, LocalHongCard.FirstSkillId, LocalHongCard.SkillCount,
            LocalHongCard.FirstEffectId, LocalHongCard.EffectCount, LocalHongCard.BalanceTemplateId,
            LocalHongCard.Name, LocalHongCard.AssetPrefix, LocalStoryChapter.Hong,
            (rank, original) => LocalHongCard.CreateCard(rank, original(LocalHongCard.TemplateId(rank)),
                original(LocalHongCard.ModelCardId), original(LocalHongCard.ForceSourceId)),
            (id, original, cards) => LocalHongCard.CreateSkill(id, original, cards(LocalHongCard.ForceSourceId).AttackType),
            LocalHongCard.CreateEffect, LocalHongCard.CreateFragment, LocalHongCard.CreatePiece, LocalHongCard.CreateBalance,
            LocalHongRelations.Create, LocalHongEquipment.Create, LocalHongEquipment.IsItem, LocalHongEquipment.MarketPreviews,
            allowUniversalFragments: false,
            createEnchantGroups: LocalHongCard.CreateEnchantGroups,
            createTranscendence: original => new[] { LocalHongCard.CreateTranscendence(original(LocalHongCard.TranscendenceTemplateId)) }
#if !BNSM_SERVER
            , applyGrowth: LocalHongCard.ApplyGrowth
#endif
            )
    });

    internal const uint UniversalFragmentId = 220001004;
    internal const string UniversalFragmentsDisabledMessage = "这张卡牌只能使用本角色碎片，无法用万能碎片补足。";
    private static readonly HashSet<int> OriginalFamiliesRequiringOwnFragments = new HashSet<int>
    {
        LocalStoryChapter.MaknaeCardId
    };

    internal static bool AllowsUniversalFragments(CardData card)
    {
        int family = card.EvolveFirstAncestor > 0 ? card.EvolveFirstAncestor : card.Id;
        return FindExtensionForCard(family)?.AllowUniversalFragments ?? !OriginalFamiliesRequiringOwnFragments.Contains(family);
    }

    // Both hosts and Unity register the same definitions. Native rows keep their
    // existing loader and cache policy, including Unity's lazy offset table.
    internal sealed class CardCatalog
    {
        private readonly Dictionary<int, Entry> registrations = new Dictionary<int, Entry>();
        private readonly List<int> ids = new List<int>();
        private readonly Dictionary<int, CardData> generated = new Dictionary<int, CardData>();
        private readonly Func<int, bool, CardData> original;
        private readonly Action<CardData, Entry> initializeGenerated;
        private readonly bool includePrototype;

        internal IReadOnlyList<int> Ids => ids;

        internal CardCatalog(IEnumerable<int> originalIds, Func<int, bool, CardData> original,
            bool includeCustom, bool includePrototype, Action<CardData, Entry> initializeGenerated = null)
        {
            this.original = original;
            this.initializeGenerated = initializeGenerated;
            this.includePrototype = includePrototype;
            foreach (int id in originalIds)
                if (!registrations.ContainsKey(id)) Add(id, null);
            if (includeCustom)
                foreach (var entry in Entries)
                    for (int id = entry.FirstCardId; id <= entry.LastCardId; id++) Add(id, entry);
            if (includePrototype) Add(LocalOriginalCardDefinition.CardId, null);
        }

        private void Add(int id, Entry entry)
        {
            if (registrations.ContainsKey(id))
                throw new InvalidDataException("Registered card ID collision: " + id);
            registrations.Add(id, entry);
            ids.Add(id);
        }

        internal CardData Get(int id, bool constCache = false)
        {
            if (!registrations.TryGetValue(id, out var entry)) return null;
            bool prototype = includePrototype && LocalOriginalCardDefinition.IsCard(id);
            if (entry == null && !prototype) return original(id, constCache);
            if (!generated.TryGetValue(id, out var card))
            {
                card = prototype
                    ? LocalOriginalCardDefinition.CreateCard(original(LocalOriginalCardDefinition.SourceCardId, true))
                    : entry.CreateCard(id - entry.FirstCardId + 1, template => original(template, true));
                if (card.Id != id) throw new InvalidDataException("Registered card has the wrong ID: " + id);
                initializeGenerated?.Invoke(card, entry);
                generated.Add(id, card);
            }
            return card;
        }
    }

    private static readonly Dictionary<int, Entry> Cards = Entries
        .SelectMany(entry => Enumerable.Range(entry.FirstCardId, entry.LastCardId - entry.FirstCardId + 1)
            .Select(id => (id, entry))).ToDictionary(x => x.id, x => x.entry);
    private static readonly Dictionary<int, Entry> Fragments = Entries.ToDictionary(x => x.FragmentId);
    private static readonly Dictionary<int, Entry> Skills = Entries
        .SelectMany(entry => Enumerable.Range(entry.FirstSkillId, entry.SkillCount).Select(id => (id, entry)))
        .ToDictionary(x => x.id, x => x.entry);
    private static readonly Dictionary<int, Entry> Effects = Entries
        .SelectMany(entry => Enumerable.Range(entry.FirstEffectId, entry.EffectCount).Select(id => (id, entry)))
        .ToDictionary(x => x.id, x => x.entry);
    private static readonly Dictionary<int, Entry> Chapters = Entries.ToDictionary(x => x.Chapter.Id);
    private static readonly Dictionary<int, Entry> Market = Entries
        .SelectMany(entry => entry.MarketRows().Select(row => (row.GoodsId, entry)))
        .ToDictionary(x => x.GoodsId, x => x.entry);

    internal static IEnumerable<RelationData> Relations() => Entries.SelectMany(entry => entry.CreateRelations());
    internal static IEnumerable<ItemData> Equipment(Func<int, ItemData> original) =>
        Entries.SelectMany(entry => entry.CreateEquipment(original));
    internal static bool IsEquipment(int id) => Entries.Any(entry => entry.IsEquipment(id));
    internal static IEnumerable<GoodsData> MarketPreviews() => Entries.SelectMany(entry => entry.MarketRows());
    internal static IEnumerable<SkillEnchantData> EnchantGroups(Func<int, SkillEnchantData> original) =>
        Entries.Where(entry => entry.CreateEnchantGroups != null).SelectMany(entry => entry.CreateEnchantGroups(original)).ToArray();
    internal static IEnumerable<TranscendenceData> TranscendenceRows(Func<int, TranscendenceData> original) =>
        Entries.Where(entry => entry.CreateTranscendence != null).SelectMany(entry => entry.CreateTranscendence(original)).ToArray();
    internal static SkillData CreatePrototypeSkill(int id, Func<int, CardData> originalCard, Func<int, SkillData> originalSkill)
    {
        int slot = id - LocalOriginalCardDefinition.FirstSkillId;
        return LocalOriginalCardDefinition.CreateSkill(originalSkill(originalCard(LocalOriginalCardDefinition.SourceCardId).ActiveSkillId[slot]), slot);
    }

    internal static Entry FindExtensionForCard(int id) => Cards.TryGetValue(id, out var entry) ? entry : null;
    internal static Entry FindFragment(int id) => Fragments.TryGetValue(id, out var entry) ? entry : null;
    internal static Entry FindChapter(int id) => Chapters.TryGetValue(id, out var entry) ? entry : null;
    internal static Entry FindMarketPreview(int id) => Market.TryGetValue(id, out var entry) ? entry : null;
    internal static Entry FindSkill(int id) => Skills.TryGetValue(id, out var entry) ? entry : null;
    internal static Entry FindEffect(int id) => Effects.TryGetValue(id, out var entry) ? entry : null;

    internal sealed class Entry
    {
        internal readonly int FirstCardId, LastCardId, CollectionId, FragmentId, FragmentTemplateId;
        internal readonly int FirstSkillId, SkillCount, FirstEffectId, EffectCount, BalanceTemplateId;
        internal readonly string Name, ArtPrefix;
        internal readonly float PortraitHeight, PortraitCenterX;
        internal readonly bool AllowUniversalFragments;
        internal readonly LocalStoryChapter Chapter;
        internal readonly Func<int, Func<int, CardData>, CardData> CreateCard;
        internal readonly Func<int, Func<int, SkillData>, Func<int, CardData>, SkillData> CreateSkill;
        internal readonly Func<int, Func<int, SkillEffectData>, SkillEffectData> CreateEffect;
        internal readonly Func<ItemData, ItemData> CreateFragment;
        internal readonly Func<int, PieceData> CreatePiece;
        internal readonly Func<BalanceCardPvpData, BalanceCardPvpData> CreateBalance;
        internal readonly Func<IEnumerable<RelationData>> CreateRelations;
        internal readonly Func<Func<int, ItemData>, IEnumerable<ItemData>> CreateEquipment;
        internal readonly Func<int, bool> IsEquipment;
        internal readonly Func<Func<int, SkillEnchantData>, IEnumerable<SkillEnchantData>> CreateEnchantGroups;
        internal readonly Func<Func<int, TranscendenceData>, IEnumerable<TranscendenceData>> CreateTranscendence;
#if !BNSM_SERVER
        internal readonly Action<RelativeStatus, int, uint, bool> ApplyGrowth;
#endif
        private readonly Func<IEnumerable<GoodsData>> equipmentPreviews;

        internal Entry(int firstCardId, int lastCardId, int collectionId, int fragmentId, int fragmentTemplateId,
            int firstSkillId, int skillCount, int firstEffectId, int effectCount, int balanceTemplateId,
            string name, string artPrefix, LocalStoryChapter chapter, Func<int, Func<int, CardData>, CardData> createCard,
            Func<int, Func<int, SkillData>, Func<int, CardData>, SkillData> createSkill,
            Func<int, Func<int, SkillEffectData>, SkillEffectData> createEffect, Func<ItemData, ItemData> createFragment,
            Func<int, PieceData> createPiece, Func<BalanceCardPvpData, BalanceCardPvpData> createBalance,
            Func<IEnumerable<RelationData>> createRelations, Func<Func<int, ItemData>, IEnumerable<ItemData>> createEquipment,
            Func<int, bool> isEquipment, Func<IEnumerable<GoodsData>> equipmentPreviews,
            float portraitHeight = .58f, float portraitCenterX = .5f, bool allowUniversalFragments = true,
            Func<Func<int, SkillEnchantData>, IEnumerable<SkillEnchantData>> createEnchantGroups = null,
            Func<Func<int, TranscendenceData>, IEnumerable<TranscendenceData>> createTranscendence = null
#if !BNSM_SERVER
            , Action<RelativeStatus, int, uint, bool> applyGrowth = null
#endif
            )
        {
            FirstCardId = firstCardId;
            LastCardId = lastCardId;
            CollectionId = collectionId;
            FragmentId = fragmentId;
            FragmentTemplateId = fragmentTemplateId;
            FirstSkillId = firstSkillId;
            SkillCount = skillCount;
            FirstEffectId = firstEffectId;
            EffectCount = effectCount;
            BalanceTemplateId = balanceTemplateId;
            Name = name;
            ArtPrefix = artPrefix;
            PortraitHeight = portraitHeight;
            PortraitCenterX = portraitCenterX;
            AllowUniversalFragments = allowUniversalFragments;
            Chapter = chapter;
            CreateCard = createCard;
            CreateSkill = createSkill;
            CreateEffect = createEffect;
            CreateFragment = createFragment;
            CreatePiece = createPiece;
            CreateBalance = createBalance;
            CreateRelations = createRelations;
            CreateEquipment = createEquipment;
            IsEquipment = isEquipment;
            CreateEnchantGroups = createEnchantGroups;
            CreateTranscendence = createTranscendence;
#if !BNSM_SERVER
            ApplyGrowth = applyGrowth;
#endif
            this.equipmentPreviews = equipmentPreviews;
        }

        // This range is separate from the original shop and the item-based local offers.
        internal int CardOfferId => checked(2_000_000_000 + CollectionId * 2);
        internal int FragmentOfferId => CardOfferId + 1;

        internal IEnumerable<GoodsData> MarketRows()
        {
            yield return MarketPreview(false);
            yield return MarketPreview(true);
            foreach (GoodsData gear in equipmentPreviews()) yield return gear;
        }

        private GoodsData MarketPreview(bool fragment) => new GoodsData
        {
            GoodsId = fragment ? FragmentOfferId : CardOfferId,
            ProductIdType = fragment ? GOODS_TYPE.GOODS_TYPE_ITEM : GOODS_TYPE.GOODS_TYPE_CARD,
            ProductId = fragment ? FragmentId : FirstCardId,
            NameAlias = fragment ? Name + "\n碎片" : Name,
            Quantity = 1,
            PcLevelMin = 1,
            PcLevelMax = 999
        };
    }
}
