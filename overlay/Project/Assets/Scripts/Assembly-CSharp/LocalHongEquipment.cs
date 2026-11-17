#if BNSM_SERVER
extern alias game;
using game::BscTable;
using static game::BscTable.EQEFFECT_TYPE;
#else
using BscTable;
using static BscTable.EQEFFECT_TYPE;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class LocalHongEquipment
{
    internal const int FirstId = 1900044000;
    private const int TierWidth = 20;
    private const int FamilyWidth = 100;
    private const int BlankStart = FirstId + 290;
    private const int PreviewStart = 2000001000;

    private static readonly Family[] Families =
    {
        new Family("孤擎耳环", 19, new[] {109076969,109076988}, new[]
        {
            new Stat(EQEFFECT_F_ATTACK,22,305), new Stat(EQEFFECT_HIT,14,226),
            new Stat(EQEFFECT_CRITICAL,11,89), new Stat(EQEFFECT_PENETRATION,46,262),
            new Stat(EQEFFECT_IMMUNE_STUN,20,20), new Stat(EQEFFECT_ADD_DAMAGE,0,950),
            new Stat(EQEFFECT_HIT_STUN,20,20)
        }),
        new Family("孤擎项链", 19, new[] {109077007,109077026}, new[]
        {
            new Stat(EQEFFECT_HP,136,19500), new Stat(EQEFFECT_AVOID,17,199, new[] {109075002,109075020}),
            new Stat(EQEFFECT_CRITICAL,11,87), new Stat(EQEFFECT_PENETRATION,46,245, new[] {109075002,109075020}),
            new Stat(EQEFFECT_IMMUNE_KNEEL,20,20), new Stat(EQEFFECT_ADD_DAMAGE,8,107, curveType:EQEFFECT_REDUCE_DAMAGE)
        }),
        new Family("孤擎拳甲", 20, new[] {109074038,109074057,109079425}, new[]
        {
            new Stat(EQEFFECT_F_ATTACK,34,588), new Stat(EQEFFECT_AVOID,16,256),
            new Stat(EQEFFECT_HIT,15,318, new[] {109076929,109076949,109080497}),
            new Stat(EQEFFECT_CRITICAL,11,107, new[] {109076929,109076949,109080497}),
            new Stat(EQEFFECT_HIT_STUN,20,24), new Stat(EQEFFECT_ADD_DAMAGE,8,107, new[] {109076929,109076949,109080497}),
            new Stat(EQEFFECT_IMMUNE_STUN,50,60), new Stat(EQEFFECT_IMMUNE_KNEEL,50,60)
        })
    };

    internal static bool IsItem(int id)
    {
        int offset = id - FirstId;
        if (offset >= 290 && offset < 290 + Families.Length) return true;
        if (offset < 0 || offset / FamilyWidth >= Families.Length) return false;
        Family family = Families[offset / FamilyWidth];
        int within = offset % FamilyWidth;
        return within / TierWidth < family.Templates.Length && within % TierWidth < family.Steps;
    }

    internal static bool IsBlank(int id) => id >= BlankStart && id < BlankStart + Families.Length;
    internal static IEnumerable<int> Blanks => Enumerable.Range(BlankStart, Families.Length);
    internal static IEnumerable<GoodsData> MarketPreviews()
    {
        for (int i = 0; i < Families.Length; i++)
            yield return new GoodsData
            {
                GoodsId = PreviewStart + i, ProductIdType = GOODS_TYPE.GOODS_TYPE_ITEM,
                ProductId = Id(i, 0, 0), NameAlias = Families[i].Name, Quantity = 1, PcLevelMin = 1, PcLevelMax = 999
            };
    }

    internal static IEnumerable<ItemData> Create(Func<int, ItemData> original)
    {
        for (int index = 0; index < Families.Length; index++)
        {
            Family family = Families[index];
            var chains = new Dictionary<int, ItemData[]>();
            ItemData[] Chain(int start)
            {
                if (chains.TryGetValue(start, out var cached)) return cached;
                var rows = new ItemData[family.Steps];
                int id = start;
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = original(id);
                    if (row == null || row.ItemType != ITEM_TYPE.ITEM_TYPE_EQUIPMENT || row.Step != i + 1)
                        throw new InvalidDataException($"Invalid original equipment chain {start}/{i + 1}");
                    rows[i] = row;
                    id = row.Next1Id;
                }
                chains.Add(start, rows);
                return rows;
            }

            var tiers = family.Templates.Select(Chain).ToArray();
            var ids = new Dictionary<int, int>();
            for (int tier = 0; tier < tiers.Length; tier++)
                for (int step = 0; step < family.Steps; step++)
                    ids.Add(tiers[tier][step].Id, Id(index, tier, step));

            for (int tier = 0; tier < tiers.Length; tier++)
            {
                for (int step = 0; step < family.Steps; step++)
                {
                    var row = LocalHongCard.Copy(tiers[tier][step]);
                    row.Id = Id(index, tier, step);
                    row.IdAlias = Alias(row.Id);
                    row.NameAlias = (tier == 0 ? "究极" : tier == 1 ? "神·" : "超·") + family.Name +
                        (step == 0 ? "" : "+" + step);
                    row.DescAlias = "孤擎万钧 洪玄公专属装备。通过无日峰·孤擎遗锻取得装备坯，沿原有锻造与觉醒路线成长。";
                    row.CardId = LocalHongCard.FirstCardId;
                    row.CardIdAlias = "Card.Local.HongXuangong.1";
                    row.Class = row.ItemSubType == ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON
                        ? CARD_CLASS.CLASS_KUNGFU_MASTER : CARD_CLASS.CLASS_ALL;
                    row.CardFaction = CARD_FACTION.FACTION_JUNGPA;
                    row.Source.Clear();
                    row.Next1Id = row.Next1Id == 0 ? 0 : ids[row.Next1Id];
                    row.Next1IdAlias = row.Next1Id == 0 ? "" : Alias(row.Next1Id);
                    row.AwakningNextId = row.AwakningNextId == 0 ? 0 : ids[row.AwakningNextId];
                    row.AwakningNextIdAlias = row.AwakningNextId == 0 ? "" : Alias(row.AwakningNextId);
                    row.EvolveFirstAncestor = ids[row.EvolveFirstAncestor];
                    row.AwakeFirstAncestor = ids[row.AwakeFirstAncestor];
                    ReplaceFragment(row.Material1Id, row.Material1IdAlias);
                    ReplaceFragment(row.AwakningMaterialId, row.AwakningMaterialIdAlias);
                    SetStats(row, family, tier, step, Chain);
                    yield return row;
                }
            }

            var blank = LocalHongCard.Copy(tiers[0][0]);
            blank.Id = BlankStart + index;
            blank.IdAlias = Alias(blank.Id);
            blank.NameAlias = family.Name + "坯";
            blank.DescAlias = "无日峰遗锻所得。用副本取得的锻材，在背包锻造成对应专属装备。";
            blank.CardId = LocalHongCard.FirstCardId;
            blank.CardIdAlias = "Card.Local.HongXuangong.1";
            blank.Class = CARD_CLASS.CLASS_NONE;
            blank.CardFaction = CARD_FACTION.FACTION_JUNGPA;
            blank.EvolveFirstAncestor = blank.AwakeFirstAncestor = blank.Id;
            blank.Next1Id = Id(index, 0, 0);
            blank.Next1IdAlias = Alias(blank.Next1Id);
            blank.AwakningNextId = 0;
            blank.AwakningNextIdAlias = "";
            blank.Source.Clear();
            ClearStats(blank);
            yield return blank;
        }
    }

    private static void SetStats(ItemData row, Family family, int tier, int step, Func<int, ItemData[]> chain)
    {
        ClearStats(row);
        foreach (Stat stat in family.Stats)
        {
            int[] templates = stat.Templates ?? family.Templates;
            int finalTier = templates.Length - 1;
            var last = chain(templates[finalTier]);
            int firstRef = Value(last[0], stat.CurveType) ?? 0;
            int endRef = Value(last[last.Length - 1], stat.CurveType) ?? firstRef;
            int currentRef = Value(last[step], stat.CurveType) ?? 0;
            double progress = firstRef == endRef ? (double)step / (last.Length - 1)
                : (double)(currentRef - firstRef) / (endRef - firstRef);
            double value = stat.First + (stat.Last - stat.First) * Math.Min(1, Math.Max(0, progress));
            if (tier < finalTier && currentRef > 0)
            {
                int? earlier = Value(chain(templates[tier])[step], stat.CurveType);
                if (earlier.HasValue) value *= Math.Min(1, (double)earlier.Value / currentRef);
            }
            row.AttributeValueIdType.Add(stat.Type);
            row.AttributeUnit.Add(EQEFFECT_UNIT_TYPE.EQEFFECT_UNIT_DIFF);
            row.AttributeValueId.Add((int)Math.Round(value, MidpointRounding.AwayFromZero));
        }
    }

    private static int? Value(ItemData row, EQEFFECT_TYPE type)
    {
        int index = row.AttributeValueIdType.IndexOf(type);
        return index < 0 ? (int?)null : row.AttributeValueId[index];
    }

    private static void ReplaceFragment(List<int> ids, List<string> aliases)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] != LocalHongCard.FragmentTemplateId) continue;
            ids[i] = LocalHongCard.FragmentId;
            if (i < aliases.Count) aliases[i] = "Item.Local.HongXuangong.Fragment";
        }
    }

    private static void ClearStats(ItemData row)
    {
        row.AttributeValueIdType.Clear();
        row.AttributeValueIdAlias.Clear();
        row.AttributeValueId.Clear();
        row.AttributeUnit.Clear();
    }

    private static int Id(int family, int tier, int step) => FirstId + family * FamilyWidth + tier * TierWidth + step;
    private static string Alias(int id) => "Item.Local.HongXuangong.Equipment." + id;

    private sealed class Family
    {
        internal readonly string Name;
        internal readonly int Steps;
        internal readonly int[] Templates;
        internal readonly Stat[] Stats;
        internal Family(string name, int steps, int[] templates, Stat[] stats)
        {
            Name = name;
            Steps = steps;
            Templates = templates;
            Stats = stats;
        }
    }

    private sealed class Stat
    {
        internal readonly EQEFFECT_TYPE Type, CurveType;
        internal readonly int First, Last;
        internal readonly int[] Templates;
        internal Stat(EQEFFECT_TYPE type, int first, int last, int[] templates = null, EQEFFECT_TYPE? curveType = null)
        {
            Type = type;
            CurveType = curveType ?? type;
            First = first;
            Last = last;
            Templates = templates;
        }
    }
}
