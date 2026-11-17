extern alias game;
using ProtoBuf;
using System.Reflection;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// 只读战斗公式审计：导出 BattleLogicConfig（命中/暴击/格挡/伤害的换算常量）、
/// NumberTable（全局数值常量）、SkillEffectTable（技能效果明细）。
/// 这些是 SkillEffect.StatusPer 系列公式的唯一数据来源，决定 1 点属性等于多少百分比。
/// </summary>
internal static class CombatFormulaAudit
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static void Run(string root)
    {
        string dir = Path.Combine(root, "evidence/analysis");
        Directory.CreateDirectory(dir);

        // ---------- 1. 战斗逻辑配置 ----------
        var logic = ReadTable<BscTable.BattleLogicConfigTable>(root, "BattleLogicConfig");
        var logicRows = new List<object>();
        foreach (var item in logic.List)
        {
            var dict = new Dictionary<string, object>();
            foreach (var p in typeof(BscTable.BattleLogicConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try { dict[p.Name] = p.GetValue(item) ?? ""; } catch { }
            }
            foreach (var f in typeof(BscTable.BattleLogicConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name.StartsWith("_")) continue;
                try { dict[f.Name] = f.GetValue(item)?.ToString() ?? ""; } catch { }
            }
            logicRows.Add(new { mode = item.GameMode.ToString(), values = dict });
        }
        File.WriteAllText(Path.Combine(dir, "battle-config.json"),
            JsonSerializer.Serialize(new { count = logicRows.Count, configs = logicRows }, Json));

        // ---------- 2. 全局数值常量 ----------
        var numbers = ReadTable<BscTable.NumberDataTable>(root, "NumberTable");
        var numRows = numbers.List
            .Select(x => new { name = x.Alias.ToString(), value = x.Num })
            .OrderBy(x => x.name)
            .ToList();
        File.WriteAllText(Path.Combine(dir, "numbers.json"),
            JsonSerializer.Serialize(new { count = numRows.Count, numbers = numRows }, Json));

        // ---------- 3. 技能效果明细 ----------
        var effects = ReadTable<BscTable.SkillEffectDataTable>(root, "SkillEffectTable");
        var effRows = new List<object>();
        foreach (var e in effects.List)
        {
            effRows.Add(new
            {
                id = e.Id,
                useType = e.UseType.ToString(),
                duration = e.Duration,
                maxStack = e.MaxStack,
                hitPer = e.HitPer,
                effectTypes = e.EffectType.Select(t => t.ToString().Replace("SEFFECT_", "")).ToList(),
                effectValues = e.EffectValue,
                effectRandValues = e.EffectRandValue,
                effectUnits = e.EffectValueUnit.Select(u => u.ToString().Replace("SEFFECT_UNIT_", "")).ToList(),
                eventType = e.EventType.ToString(),
                eventProb = e.EventProb,
                eventSkillId = e.EventSkillId,
                filterId = e.EffectFilterId
            });
        }
        File.WriteAllText(Path.Combine(dir, "skill-effects.json"),
            JsonSerializer.Serialize(new { count = effRows.Count, effects = effRows }, Json));

        // ---------- 4. 顶级装备词条带单位（判断 DIFF/PER，避免把百分比当固定值） ----------
        var cat = new GameCatalog(root);
        string T2(string alias) => string.IsNullOrEmpty(alias) ? "" : (alias);
        var topItems = new List<object>();
        foreach (var it in cat.Items.OrderByDescending(x => x.AttributeValueId.Sum(v => Math.Abs(v))))
        {
            if (it.AttributeValueId.Count == 0) continue;
            var attrs = new List<object>();
            for (int i = 0; i < it.AttributeValueId.Count && i < it.AttributeValueIdType.Count; i++)
            {
                attrs.Add(new
                {
                    type = it.AttributeValueIdType[i].ToString().Replace("EQEFFECT_", ""),
                    value = it.AttributeValueId[i],
                    unit = i < it.AttributeUnit.Count ? it.AttributeUnit[i].ToString().Replace("EQEFFECT_UNIT_", "") : "?"
                });
            }
            topItems.Add(new
            {
                id = it.Id,
                type = it.ItemType.ToString(),
                sub = it.ItemSubType.ToString(),
                grade = it.Grade.ToString(),
                equip = it.EquipmentType.ToString(),
                exclusive = it.IsExclusiveItem(),
                cardId = it.CardId,
                attrs
            });
        }
        File.WriteAllText(Path.Combine(dir, "equip-units.json"),
            JsonSerializer.Serialize(new { count = topItems.Count, items = topItems }, Json));

        Console.WriteLine($"Combat formula audit: {logicRows.Count} configs, {numRows.Count} numbers, {effRows.Count} effects, {topItems.Count} equip-with-units -> {dir}");
    }

    static T ReadTable<T>(string root, string name)
    {
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", name + ".pb"));
        return Serializer.Deserialize<T>(input);
    }
}
