#if BNSM_SERVER
extern alias game;
using game::BscTable;
using static game::BscTable.SEFFECT_TYPE;
#else
using BscTable;
using static BscTable.SEFFECT_TYPE;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;

public static class LocalHongCard
{
    public const int FirstCardId = 1900040001;
    public const int LastCardId = FirstCardId + 15;
    public const int CollectionId = 147;
    public const int FragmentId = 1900041001;
    public const int FirstSkillId = 1900042001;
    public const int FirstEffectId = 1900043001;
    public const int DodgeSpiritHealEffectId = FirstEffectId + 19;
    public const int EnchantGroupId = 1900046001;
    public const int EnchantTemplateId = 118;
    public const int TranscendenceTemplateId = 31168;
    private const int EnchantSteps = 5;
    public const int EnchantSlots = 6;
    private const int FirstEnchantSkillOffset = 11;
    private const int FirstEnchantEffectOffset = 21;
    private const int ExtraEnchantSkillOffset = FirstEnchantSkillOffset + EnchantSteps;
    private const int ExtraEnchantEffectOffset = FirstEnchantEffectOffset + EnchantSteps * 2;
    private const int ExtraSkillsPerStep = 8;
    private const int ExtraEffectsPerStep = 9;
    public const int SkillCount = ExtraEnchantSkillOffset + EnchantSteps * ExtraSkillsPerStep;
    public const int EffectCount = ExtraEnchantEffectOffset + EnchantSteps * ExtraEffectsPerStep;
    private const int EnchantTemplateFragmentId = 205081309;
    private const string FragmentAlias = "Item.Local.HongXuangong.Fragment";
    // Ultimate and awakening lack original enchant rows. Reuse the active and
    // passive recipe schedules while keeping their own distinct skill variants.
    private static readonly int[] EnchantRecipeTemplates = { EnchantTemplateId, EnchantTemplateId, 5105, 10118, 10143, 10143 };
    private const float FirstDamageGrowth = (3100 - 266) / 98f;
    public const int ModelCardId = 30121;
    public const int BalanceTemplateId = 31128;
    public const int HealthSourceId = 30161;
    public const int ForceSourceId = 50301;
    public const int FragmentTemplateId = 205081296;
    public const string Name = "孤擎万钧 洪玄公";
    public const string Model = "3DCH9023";
    public const string AssetPrefix = "LocalHongXuangong";
    public const string Intro = "秦义绝为夺鬼天剑，兵临无日峰。祸乱横生，洪门弟子惨遭屠戮。目睹洪门濒临覆灭，洪玄公被逼唤醒蛰伏之力，惊天伟力骤然迸发，刹那间骤雨疾风，力王现世！";
    private const float GuardDuration = 10;

    private static string ArtForRank(int rank) =>
        AssetPrefix + (rank >= 9 ? "Awakened" : rank >= 5 ? "FiveStar" : "Default");

    public static bool IsCard(int id) => id >= FirstCardId && id <= LastCardId;
    public static bool IsSkill(int id) => id >= FirstSkillId && id < FirstSkillId + SkillCount;
    public static bool IsEffect(int id) => id >= FirstEffectId && id < FirstEffectId + EffectCount;
    public static int TemplateId(int rank) => rank <= 8 ? HealthSourceId + rank - 1 : 31161 + rank - 9;

    public static T Copy<T>(T original)
    {
        using (var stream = new MemoryStream())
        {
            Serializer.Serialize(stream, original);
            stream.Position = 0;
            return Serializer.Deserialize<T>(stream);
        }
    }

#if !BNSM_SERVER
    internal static void ApplyGrowth(RelativeStatus result, int rank, uint level, bool raidBoss)
    {
        var tables = Singleton<DataTable>.Instance;
        var health = tables.GetCardData(HealthSourceId, false);
        var force = tables.GetCardData(ForceSourceId, false);
        int forceRankId = rank <= 8 ? ForceSourceId + rank - 1 : 51301 + rank - 9;
        var healthBase = health.GetBaseStatus(1, 0, raidBoss, false);
        var forceBase = force.GetBaseStatus(1, 0, raidBoss, false);
        var forceGrowth = tables.GetCardData(forceRankId, false).GetBaseStatus(level, 0, raidBoss, false);

        result.stat[1].diff += 266 - healthBase.stat[1].diff;
        result.stat[3].diff = 56 + forceGrowth.stat[3].diff - forceBase.stat[3].diff;
        result.stat[4].diff += 10 - healthBase.stat[4].diff;
        result.stat[5].diff += 10 - healthBase.stat[5].diff;
        result.stat[6].diff += 8 - healthBase.stat[6].diff;
        result.stat[7].diff += 16 - healthBase.stat[7].diff;
        result.stat[8].diff = 10 + forceGrowth.stat[8].diff - forceBase.stat[8].diff;
        result.stat[9].diff += 4 - healthBase.stat[9].diff;
        result.stat[10].diff += 80 - healthBase.stat[10].diff;
        result.stat[11].diff += 12 - healthBase.stat[11].diff;
    }

#endif

    public static CardData CreateCard(int rank, CardData template, CardData model, CardData force)
    {
        if (rank < 1 || rank > 16 || template.Id != TemplateId(rank) || model.Id != ModelCardId || force.Id != ForceSourceId)
            throw new InvalidDataException("Missing original Hong Xuangong card references");
        var card = Copy(template);
        card.Id = FirstCardId + rank - 1;
        card.IdAlias = "Card.Local.HongXuangong." + rank;
        card.NameAlias = card.SortNameAlias = Name;
        card.DescAlias = Intro;
        card.MemoAlias = "无日峰风雨中现世的洪门力王。";
        card.SimpleMemoAlias = "前排驱散、闪避反击、浮空连招";
        card.Talent = CARD_TALENT.CARD_TALENT_SS;
        card.Faction = CARD_FACTION.FACTION_JUNGPA;
        card.AttackType = force.AttackType;
        card.CollectionId = CollectionId;
        card.EvolveFirstAncestor = FirstCardId;
        card.RankUpCardId = rank < 16 ? card.Id + 1 : 0;
        card.PieceId = FragmentId;
        card.PieceIdAlias = FragmentAlias;
        card.LinkMissonId = card.TalentUpCardId = 0;
        card.RecommendEquipmentID = 0;
        card.BaseEquipmentWId = model.BaseEquipmentWId;
        card.BaseEquipmentNId = model.BaseEquipmentNId;
        card.BaseEquipmentEId = model.BaseEquipmentEId;
        card.Source.Clear();
        card.Minimi = model.Minimi;
        card.MinimiActSet = model.MinimiActSet;
        card.VoiceSet = model.VoiceSet;
        card.SpawnVoice = model.SpawnVoice;
        card.MinimiSpawnVoice = model.MinimiSpawnVoice;
        string art = ArtForRank(rank);
        card.CardMini = card.CardSmall = card.CardBig = card.Character = card.CardBattle = art;
        card.CardMiniAtlas = card.CardBattleAtlas = art + "Atlas";
        card.Illustrator = "玩家供图 · AI 辅助处理";
        card.ActiveSkillId.Clear();
        card.ActiveSkillId.AddRange(new[] { FirstSkillId, FirstSkillId + 1 });
        card.ActiveSkillIdAlias.Clear();
        card.ActiveSkillIdAlias.AddRange(new[] { "洪门神功", "极限洪门神功" });
        card.PassiveSkillId.Clear();
        card.PassiveSkillId.AddRange(new[] { FirstSkillId + 2, FirstSkillId + 3, FirstSkillId + 4, FirstSkillId + 5 });
        card.PassiveSkillIdAlias.Clear();
        card.PassiveSkillIdAlias.AddRange(new[] { "浮空", "浮空连招", "青龙信念", "潜力觉醒" });
        card.SkillEnchantGroupId.Clear();
        card.SkillEnchantGroupId.AddRange(new int[7]);
        if (rank >= 8)
            for (int slot = 0; slot < EnchantSlots; slot++) card.SkillEnchantGroupId[slot] = EnchantGroupId + slot;
        return card;
    }

    public static IEnumerable<SkillEnchantData> CreateEnchantGroups(Func<int, SkillEnchantData> original)
    {
        for (int slot = 0; slot < EnchantSlots; slot++)
        {
            var group = Copy(original(EnchantRecipeTemplates[slot]));
            group.SkillEnchantGroupID = EnchantGroupId + slot;
            foreach (var row in group.SkillEnchantInfoList)
            {
                int previous = row.EnchantLevel == 1 ? FirstSkillId + slot : EnchantSkillId(slot, row.EnchantLevel - 2);
                int next = EnchantSkillId(slot, row.EnchantLevel - 1);
                row.PrevEnchantSkillId = previous;
                row.NextEnchantSkillId = next;
                row.PrevEnchantSkillIdAlias = "Skill.Local.HongXuangong." + (previous - FirstSkillId);
                row.NextEnchantSkillIdAlias = "Skill.Local.HongXuangong." + (next - FirstSkillId);
                for (int i = 0; i < row.MaterialItemId.Count; i++)
                {
                    if (row.MaterialItemId[i] != FragmentTemplateId && row.MaterialItemId[i] != EnchantTemplateFragmentId) continue;
                    row.MaterialItemId[i] = FragmentId;
                    if (i < row.MaterialItemIdAlias.Count) row.MaterialItemIdAlias[i] = FragmentAlias;
                }
            }
            yield return group;
        }
    }

    private static int EnchantSkillId(int slot, int step) => FirstSkillId + (slot == 0
        ? FirstEnchantSkillOffset + step : ExtraEnchantSkillOffset + step * ExtraSkillsPerStep + slot - 1);

    public static TranscendenceData CreateTranscendence(TranscendenceData template)
    {
        var row = Copy(template);
        row.CardId = LastCardId;
        row.CardIdAlias = "Card.Local.HongXuangong.16";
        row.GrowthItemId = row.GrowthCancelItemId = FragmentId;
        row.GrowthItemIdAlias = row.GrowthCancelItemIdAlias = FragmentAlias;
        foreach (var effect in row.EffectDataList)
            foreach (var grade in effect.EffectGradeDataList)
                if (grade.GradeItemId == FragmentTemplateId)
                {
                    grade.GradeItemId = FragmentId;
                    grade.GradeItemIdAlias = FragmentAlias;
                }
        return row;
    }

    public static BalanceCardPvpData CreateBalance(BalanceCardPvpData template)
    {
        var row = Copy(template);
        row.CardId = LastCardId;
        return row;
    }

    public static ItemData CreateFragment(ItemData original)
    {
        var item = Copy(original);
        item.Id = FragmentId;
        item.IdAlias = FragmentAlias;
        item.NameAlias = Name + "碎片";
        item.DescAlias = "在无日峰剧情副本中取得，可用于孤擎万钧洪玄公升星、技能强化和超越。";
        item.Icon = ArtForRank(1);
        item.Atlas = item.Icon + "Atlas";
        item.CardId = FirstCardId;
        item.CardIdAlias = "Card.Local.HongXuangong.1";
        item.CardFaction = CARD_FACTION.FACTION_JUNGPA;
        return item;
    }

    public static PieceData CreatePiece(int count) => new PieceData {
        ItemId = FragmentId, ItemIdAlias = FragmentAlias, Complete = count,
        NextId = FirstCardId, NextIdAlias = "Card.Local.HongXuangong.1", NextIdType = REWARD_TYPE.REWARD_TYPE_CARD
    };

    public static SkillData CreateSkill(int id, Func<int, SkillData> original, ATTACK_TYPE attackType)
    {
        int slot = id - FirstSkillId;
        if (slot >= FirstEnchantSkillOffset)
        {
            return CreateEnchantedSkill(id, slot, original, attackType);
        }
        var skill = Copy(original(new[] { 13570, 13577, 13874, 15080, 13879, 81, 13876, 13842, 13875, 13843, 15081 }[slot]));
        skill.Id = id;
        skill.IdAlias = "Skill.Local.HongXuangong." + slot;
        skill.AttackType = attackType;
        skill.EnchantLevel = 0;
        skill.NameAlias = new[] { "洪门神功", "极限洪门神功", "浮空", "浮空连招", "青龙信念", "潜力觉醒",
            "浮空", "洪门身法", "闪避反击", "闪避回气", "浮空连招" }[slot];
        skill.DescAlias = new[] {
            "提高自身闪避，解除敌方前排增益并造成气功伤害，降低虚弱和眩晕抗性。",
            "大幅提高自身闪避，为同排队友恢复内力，解除敌方前排增益并降低虚弱和眩晕抗性。",
            "对眩晕敌人造成伤害并使其浮空。开战时提高闪避；闪避后有概率反击眩晕和恢复内力。",
            "对浮空的敌人施展终结技。", "提高生命、闪避、气功攻击和青龙浮空抗性。",
            "提高气功攻击，两项命中增益叠加，并提升眩晕、虚弱抗性。",
            "攻击眩晕目标并使其浮空。", "开战后 6 秒内提高闪避。", "解除增益并造成伤害、眩晕和内力损失。",
            "闪避时恢复自身内力。", "对浮空的敌人施展终结技。"
        }[slot];
        int[][] effects = {
            new[] {0, 1}, new[] {3, 4}, new[] {7, 8, 9, 10}, new[] {11}, new[] {12}, new[] {13},
            new[] {14, 15}, new[] {16}, new[] {17, 18}, new[] {19}, new[] {20}
        };
        skill.SkillEffectId.Clear();
        skill.SkillEffectIdAlias.Clear();
        foreach (int effect in effects[slot]) skill.SkillEffectId.Add(FirstEffectId + effect);
        skill.CasterSkillEffectId.Clear();
        skill.CasterSkillEffectIdAlias.Clear();
        skill.CasterSkillRange.Clear();
        if (slot <= 1)
        {
            skill.TargetType = TARGET_TYPE.TARGET_ENEMY;
            skill.TargetRange = TARGET_RANGE.TARGET_ALL;
            skill.TargetPosition = TARGET_POSITION.TP_FRONT;
            skill.CasterSkillEffectId.Add(FirstEffectId + (slot == 0 ? 2 : 5));
            skill.CasterSkillRange.Add(CASTER_EFFECT_RANGE.CE_CASTER);
            if (slot == 1)
            {
                skill.CasterSkillEffectId.Add(FirstEffectId + 6);
                skill.CasterSkillRange.Add(CASTER_EFFECT_RANGE.CE_CASTER_LINE_EXCEPT_CASTER);
            }
        }
        if (slot == 10)
        {
            skill.ForceType = SKILL_FORCE_TYPE.SKILL_FORCE_DEFAULT;
            skill.FinalSkill = true;
        }
        return skill;
    }

    private static SkillData CreateEnchantedSkill(int id, int slot, Func<int, SkillData> original, ATTACK_TYPE attackType)
    {
        int extra = slot - ExtraEnchantSkillOffset;
        int step = extra < 0 ? slot - FirstEnchantSkillOffset : extra / ExtraSkillsPerStep;
        int kind = extra < 0 ? -1 : extra % ExtraSkillsPerStep;
        int baseSlot = kind switch { -1 => 0, 5 => 6, 6 => 8, 7 => 10, _ => kind + 1 };
        var skill = CreateSkill(FirstSkillId + baseSlot, original, attackType);
        skill.Id = id;
        skill.IdAlias = "Skill.Local.HongXuangong." + slot;
        skill.EnchantLevel = step + 1;
        int effect = FirstEffectId + ExtraEnchantEffectOffset + step * ExtraEffectsPerStep;
        switch (kind)
        {
            case -1:
                skill.SkillEffectId[0] = FirstEffectId + FirstEnchantEffectOffset + step * 2;
                skill.CasterSkillEffectId[0] = skill.SkillEffectId[0] + 1;
                break;
            case 0: skill.SkillEffectId[0] = effect; break;
            case 1:
                skill.SkillEffectId[0] = effect + 1;
                skill.SkillEffectId[2] = effect + 2;
                break;
            case 2: skill.SkillEffectId[0] = effect + 3; break;
            case 3: skill.SkillEffectId[0] = effect + 4; break;
            case 4: skill.SkillEffectId[0] = effect + 5; break;
            case 5: skill.SkillEffectId[0] = effect + 6; break;
            case 6: skill.SkillEffectId[0] = effect + 7; break;
            case 7: skill.SkillEffectId[0] = effect + 8; break;
        }
        return skill;
    }

    public static SkillEffectData CreateEffect(int id, Func<int, SkillEffectData> original)
    {
        int slot = id - FirstEffectId;
        SkillEffectData effect;
        switch (slot)
        {
            case 0: effect = Damage(190, 266, true, FirstDamageGrowth); break;
            case 1: effect = Resistance(-30); break;
            case 2: effect = Avoid(500, GuardDuration); break;
            case 3: effect = Damage(200, 268, true); break;
            case 4: effect = Resistance(-50); break;
            case 5: effect = Avoid(300, GuardDuration); break;
            case 6: effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_INSTANT, 0, SEFFECT_SP_HEAL, 10); break;
            case 7: effect = Trigger(SEFFECT_EVENT_TYPE.SEFFECT_EVENT_ENEMY_STUN, 6); break;
            case 8: effect = Trigger(SEFFECT_EVENT_TYPE.SEFFECT_EVENT_START_BATTLE, 7); break;
            case 9: effect = Copy(original(14890)); effect.EventSkillId = FirstSkillId + 8; effect.EventSkillIdAlias = ""; break;
            case 10: effect = Copy(original(14822)); effect.EventSkillId = FirstSkillId + 9; effect.EventSkillIdAlias = ""; break;
            case 11: effect = Trigger(SEFFECT_EVENT_TYPE.SEFFECT_EVENT_ENEMY_AIR, 10); break;
            case 12:
                effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_PASSIVE, 0, SEFFECT_ABILITY_HP, 1800, growth: 16);
                Add(effect, SEFFECT_ABILITY_AVOID, 20, growth: 2);
                Add(effect, SEFFECT_ABILITY_FORCE_ATTACK, 188, growth: 2);
                Add(effect, SEFFECT_ABILITY_IMMUNE_AIR, 70);
                break;
            case 13:
                effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_PASSIVE, 0, SEFFECT_ABILITY_FORCE_ATTACK, 28, growth: 2);
                Add(effect, SEFFECT_ABILITY_HIT, 55, growth: 5);
                Add(effect, SEFFECT_ABILITY_HIT, 55, growth: 5);
                Add(effect, SEFFECT_ABILITY_IMMUNE_STUN, 10);
                Add(effect, SEFFECT_ABILITY_IMMUNE_KNEEL, 10);
                break;
            case 14: effect = Damage(100, 136, false); break;
            case 15: effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_DEBUFF_BLOCK, 3, SEFFECT_AIR, 200, visual: SEFFECT_VISUAL.SEFFECT_VISUAL_AIR); break;
            case 16: effect = Avoid(800, 6); break;
            case 17: effect = Damage(100, 138, true); break;
            case 18:
                effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_DEBUFF_BLOCK, 3, SEFFECT_STUN, 200, visual: SEFFECT_VISUAL.SEFFECT_VISUAL_STUN);
                Add(effect, SEFFECT_SP_ATTACK, 10);
                break;
            case 19: effect = Copy(original(14824)); break;
            case 20: effect = Copy(original(15290)); break;
            default:
                if (slot >= ExtraEnchantEffectOffset && slot < EffectCount)
                {
                    effect = CreateExtraEnchantEffect(slot - ExtraEnchantEffectOffset, original);
                    break;
                }
                if (slot < FirstEnchantEffectOffset || slot >= EffectCount) throw new ArgumentOutOfRangeException(nameof(id));
                int step = (slot - FirstEnchantEffectOffset) / 2;
                if ((slot - FirstEnchantEffectOffset) % 2 == 1)
                    effect = Avoid(550 + step * 50, GuardDuration);
                else
                {
                    // Apply the original weapon-art enchant increments to Hong's own base.
                    var baseline = original(15283);
                    var enhanced = original(63546 + step * 8);
                    effect = Damage(190 + enhanced.EffectValue[0] - baseline.EffectValue[0],
                        266 + enhanced.EffectValue[1] - baseline.EffectValue[1], true,
                        FirstDamageGrowth + enhanced.LvUpValue[1] - baseline.LvUpValue[1]);
                }
                break;
        }
        effect.Id = id;
        effect.IdAlias = "Effect.Local.HongXuangong." + slot;
        return effect;
    }

    private static SkillEffectData CreateExtraEnchantEffect(int offset, Func<int, SkillEffectData> original)
    {
        int step = offset / ExtraEffectsPerStep, tier = step + 1;
        int skill = FirstSkillId + ExtraEnchantSkillOffset + step * ExtraSkillsPerStep;
        switch (offset % ExtraEffectsPerStep)
        {
            case 0:
                // The ultimate follows the existing active damage increment, on its own baseline.
                return IncreaseDamage(Damage(200, 268, true), original(15283), original(63546 + step * 8));
            case 1: return Trigger(SEFFECT_EVENT_TYPE.SEFFECT_EVENT_ENEMY_STUN, skill + 5 - FirstSkillId);
            case 2:
                var counter = Copy(original(14890));
                counter.EventSkillId = skill + 6;
                counter.EventSkillIdAlias = "";
                return counter;
            case 3: return Trigger(SEFFECT_EVENT_TYPE.SEFFECT_EVENT_ENEMY_AIR, skill + 7 - FirstSkillId);
            case 4:
                var belief = CreateEffect(FirstEffectId + 12, original);
                var baseline = original(14898);
                var enhanced = original(63659 + step * 104);
                IncreaseStat(belief, SEFFECT_ABILITY_HP, baseline, enhanced);
                IncreaseStat(belief, SEFFECT_ABILITY_AVOID, baseline, enhanced);
                belief.EffectValue[3] += 4 * tier;
                return belief;
            case 5:
                var awakening = CreateEffect(FirstEffectId + 13, original);
                awakening.EffectValue[0] += 4 * tier;
                awakening.EffectValue[1] += 5 * tier;
                awakening.EffectValue[2] += 5 * tier;
                return awakening;
            case 6: return IncreaseDamage(Damage(100, 136, false), original(14894), original(63374 + step * 12));
            case 7: return IncreaseDamage(Damage(100, 138, true), original(14892), original(63372 + step * 12));
            default: return Copy(original(63553 + step * 8));
        }
    }

    private static SkillEffectData IncreaseDamage(SkillEffectData effect, SkillEffectData baseline, SkillEffectData enhanced)
    {
        IncreaseStat(effect, SEFFECT_POWER_ATTACK, baseline, enhanced);
        IncreaseStat(effect, SEFFECT_PIERCE_ATTACK, baseline, enhanced);
        return effect;
    }

    private static void IncreaseStat(SkillEffectData effect, SEFFECT_TYPE type, SkillEffectData baseline, SkillEffectData enhanced)
    {
        int target = effect.EffectType.IndexOf(type);
        int source = baseline.EffectType.IndexOf(type);
        int improvement = enhanced.EffectType.IndexOf(type);
        effect.EffectValue[target] += enhanced.EffectValue[improvement] - baseline.EffectValue[source];
        effect.LvUpValue[target] += enhanced.LvUpValue[improvement] - baseline.LvUpValue[source];
    }

    private static SkillEffectData Trigger(SEFFECT_EVENT_TYPE eventType, int skill) =>
        new SkillEffectData { Repeat = 1, EventType = eventType, EventProb = 100, EventSkillId = FirstSkillId + skill, HitPer = 100 };

    private static SkillEffectData Damage(int percent, int amount, bool dispel, float growth = 18f)
    {
        var effect = new SkillEffectData { Repeat = 1, UseType = SEFFECT_USE_TYPE.SEFFECT_USE_INSTANT, HitPer = 100, UseCardAttackType = true };
        if (dispel) Add(effect, SEFFECT_DISPEL_BUFF, 200);
        Add(effect, SEFFECT_POWER_ATTACK, percent, percent: true);
        Add(effect, SEFFECT_PIERCE_ATTACK, amount, growth: growth);
        return effect;
    }

    private static SkillEffectData Resistance(int points)
    {
        var effect = Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_DEBUFF_STAT, 10, SEFFECT_ABILITY_IMMUNE_KNEEL, points, visual: SEFFECT_VISUAL.SEFFECT_VISUAL_DEBUFF_ABILITY);
        Add(effect, SEFFECT_ABILITY_IMMUNE_STUN, points);
        return effect;
    }

    private static SkillEffectData Avoid(int percent, float seconds) =>
        Ripple(SEFFECT_USE_TYPE.SEFFECT_USE_BUFF_STAT, seconds, SEFFECT_ABILITY_AVOID, percent, percent: true, visual: SEFFECT_VISUAL.SEFFECT_VISUAL_BUFF_ABILITY);

    private static SkillEffectData Ripple(SEFFECT_USE_TYPE use, float duration, SEFFECT_TYPE type, int value,
        bool percent = false, int growth = 0, SEFFECT_VISUAL visual = SEFFECT_VISUAL.SEFFECT_VISUAL_NONE)
    {
        var effect = new SkillEffectData { Repeat = 1, UseType = use, Duration = duration, HitPer = 100, EffectVisual = visual };
        Add(effect, type, value, percent, growth);
        return effect;
    }

    private static void Add(SkillEffectData effect, SEFFECT_TYPE type, int value, bool percent = false, float growth = 0)
    {
        effect.EffectType.Add(type);
        effect.EffectValue.Add(value);
        effect.EffectValueUnit.Add(percent ? SEFFECT_UNIT_TYPE.SEFFECT_UNIT_PER : SEFFECT_UNIT_TYPE.SEFFECT_UNIT_DIFF);
        effect.EffectRandValue.Add(0);
        effect.LvUpValue.Add(growth);
    }
}
