extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// 只读卡牌技能审计：把每张卡的主动/被动技能连同效果数值、文案全部导出，
/// 供配队与强度分析使用。不写存档、不改游戏状态。
/// </summary>
internal static class CardSkillAudit
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static void Run(string root)
    {
        var c = new GameCatalog(root);
        string dir = Path.Combine(root, "evidence/analysis");
        Directory.CreateDirectory(dir);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "LocalServer/data/tables/manifest.json")));
        var textEntry = manifest.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt32() == game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        var texts = ReadTable<BscTable.LocalTextDataTable>(root, textEntry.GetProperty("name").GetString()!);
        texts.MakeMap();
        string T(string alias) => string.IsNullOrEmpty(alias) ? "" : (texts.GetItem(alias)?.Text ?? alias);

        var skillTable = ReadTable<BscTable.SkillDataTable>(root, "SkillDataTable");
        var skillById = skillTable.List.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        var effectTable = ReadTable<BscTable.SkillEffectDataTable>(root, "SkillEffectTable");
        var effectById = effectTable.List.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

        // 只导出可用卡（四星池里能抽到的 134 张 + 全部五星以上），其它卡技能雷同
        var rows = new List<object>();
        foreach (var card in c.Cards.OrderBy(x => x.Rank).ThenBy(x => x.Id))
        {
            if (card.Rank < 4) continue;

            var active = new List<object>();
            foreach (int sid in card.ActiveSkillId)
            {
                if (!skillById.TryGetValue(sid, out var sk)) continue;
                active.Add(DumpSkill(sk, effectById, T));
            }
            var passive = new List<object>();
            foreach (int sid in card.PassiveSkillId)
            {
                if (!skillById.TryGetValue(sid, out var sk)) continue;
                passive.Add(DumpSkill(sk, effectById, T));
            }

            rows.Add(new
            {
                id = card.Id,
                name = T(card.NameAlias),
                rank = card.Rank,
                class_ = card.Class.ToString().Replace("CARD_CLASS_", ""),
                talent = card.Talent.ToString().Replace("CARD_TALENT_", ""),
                attackType = card.AttackType.ToString().Replace("ATTACK_", ""),
                active,
                passive
            });
        }

        File.WriteAllText(Path.Combine(dir, "card-skills.json"),
            JsonSerializer.Serialize(new { count = rows.Count, cards = rows }, Json));
        Console.WriteLine($"Card skill audit: {rows.Count} cards -> {dir}");
    }

    static object DumpSkill(BscTable.SkillData sk, Dictionary<int, BscTable.SkillEffectData> effectById, Func<string, string> T)
    {
        var effs = new List<object>();
        foreach (int eid in sk.SkillEffectId)
        {
            if (!effectById.TryGetValue(eid, out var e)) { effs.Add(new { id = eid, missing = true }); continue; }
            var parts = new List<object>();
            for (int i = 0; i < e.EffectType.Count; i++)
            {
                float v = i < e.EffectValue.Count ? e.EffectValue[i] : 0;
                float lvUp = i < e.LvUpValue.Count ? e.LvUpValue[i] : 0;
                parts.Add(new
                {
                    type = e.EffectType[i].ToString().Replace("SEFFECT_", ""),
                    value = v,
                    lvUp,
                    // MAX_CARD_SKILL_LEVEL=99，但 SKILL_POINT_SYSTEM_LIMIT_COUNT=20 → 实际可到 21 级
                    valueLv21 = v + lvUp * 20,
                    rand = i < e.EffectRandValue.Count ? e.EffectRandValue[i] : 0,
                    unit = i < e.EffectValueUnit.Count ? e.EffectValueUnit[i].ToString().Replace("SEFFECT_UNIT_", "") : ""
                });
            }
            effs.Add(new
            {
                id = eid,
                duration = e.Duration,
                maxStack = e.MaxStack,
                hitPer = e.HitPer,
                useType = e.UseType.ToString().Replace("SEFFECT_USE_", ""),
                eventType = e.EventType.ToString().Replace("SEFFECT_EVENT_", ""),
                eventProb = e.EventProb,
                eventSkillId = e.EventSkillId,
                parts
            });
        }

        var casterEffs = new List<object>();
        foreach (int eid in sk.CasterSkillEffectId)
        {
            if (!effectById.TryGetValue(eid, out var ce)) { casterEffs.Add(new { id = eid, missing = true }); continue; }
            var cparts = new List<object>();
            for (int i = 0; i < ce.EffectType.Count; i++)
            {
                float v = i < ce.EffectValue.Count ? ce.EffectValue[i] : 0;
                float lu = i < ce.LvUpValue.Count ? ce.LvUpValue[i] : 0;
                cparts.Add(new
                {
                    type = ce.EffectType[i].ToString().Replace("SEFFECT_", ""),
                    value = v, lvUp = lu, valueLv21 = v + lu * 20,
                    unit = i < ce.EffectValueUnit.Count ? ce.EffectValueUnit[i].ToString().Replace("SEFFECT_UNIT_", "") : ""
                });
            }
            casterEffs.Add(new { id = eid, duration = ce.Duration, maxStack = ce.MaxStack, parts = cparts });
        }

        return new
        {
            id = sk.Id,
            name = T(sk.NameAlias),
            desc = T(sk.DescAlias),
            type = sk.SkillType.ToString().Replace("SKILL_", ""),
            attackType = sk.AttackType.ToString().Replace("ATTACK_", ""),
            target = sk.TargetType.ToString().Replace("TARGET_", ""),
            range = sk.TargetRange.ToString().Replace("TARGET_", ""),
            position = sk.TargetPosition.ToString().Replace("TP_", ""),
            hitPer = sk.SkillHitPer,
            priority = sk.TriggerPriority,
            battlePointPer = sk.BattlePointPer,
            final = sk.FinalSkill,
            effects = effs,
            casterEffects = casterEffs
        };
    }

    static T ReadTable<T>(string root, string name)
    {
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", name + ".pb"));
        return Serializer.Deserialize<T>(input);
    }
}
