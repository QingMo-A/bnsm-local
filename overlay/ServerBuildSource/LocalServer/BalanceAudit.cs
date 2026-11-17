extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// 只读平衡性审计导出：把角色基础属性、装备/宝石词条、寻宝池配置、技能表
/// 落盘到 evidence/analysis/，供后续做数值分析。不写存档、不改游戏状态。
/// </summary>
internal static class BalanceAudit
{
    static readonly byte[] Key = { 52, 199, 139, 84 };

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>CardRelativeStatusData.status 是逐 float 异或加密的，索引直接落到 EQEFFECT_TYPE。</summary>
    static float[] DecodeStatus(byte[] raw)
    {
        int n = raw.Length / 4;
        var result = new float[n];
        for (int i = 1; i < n; i++)
        {
            Span<byte> b = stackalloc byte[4];
            for (int k = 0; k < 4; k++) b[k] = (byte)(raw[i * 4 + k] ^ Key[k]);
            result[i] = BitConverter.ToSingle(b);
        }
        return result;
    }

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

        // ---------- 1. 角色属性 ----------
        var relTable = ReadTable<BscTable.CardRelativeStatusDataTable>(root, "CardRelativeStatusDataTable");
        var relById = relTable.List.ToDictionary(x => x.Id, x => DecodeStatus(x.status));
        var rankMax = c.CardRanks.GroupBy(x => x.Rank).ToDictionary(g => g.Key, g => g.Max(x => x.MaxLevel));

        // Rank 1..8 直接查属性表；9 以上统一用 rank 8 的基础值再乘阶数倍率。
        var ratioTable = ReadTable<BscTable.CardAttributeRankRatioDataTable>(root, "CardAttributeRankRatioDataTable");
        var ratioByRank = ratioTable.List.GroupBy(x => x.Rank).ToDictionary(g => g.Key, g => g.First());

        void ApplyRatio(float[] s, BscTable.CardAttributeRankRatioData r)
        {
            if (r == null) return;
            s[2] *= r.P_Attack; s[3] *= r.F_Attack; s[4] *= r.P_Defence; s[5] *= r.F_Defence;
            s[1] *= r.Hp; s[6] *= r.Hit; s[7] *= r.Avoid; s[8] *= r.Critical;
            s[9] *= r.CriticalDef; s[10] *= r.Penetration; s[11] *= r.Parry;
        }

        // 属性名取 EQEFFECT 枚举值，去掉前缀即可读。
        string EffName(int i) => i < 52 ? ((BscTable.EQEFFECT_TYPE)i).ToString().Replace("EQEFFECT_", "") : "E" + i;

        var cardRows = new List<object>();
        foreach (var card in c.Cards)
        {
            int cls = (int)card.Class;
            int rank = card.Rank;
            if (!rankMax.TryGetValue(rank, out int maxLv)) maxLv = 1;
            game::BSC_DEFINE.GetHighLowRank(rank, out int highRank, out int lowRank);
            int statRank = highRank > 0 ? game::BSC_DEFINE.CARD_MAX_LOW_RANK : lowRank;
            int idMax = ((cls << 22) + (statRank << 12) + maxLv);
            int idMin = ((cls << 22) + (statRank << 12) + 1);
            if (!relById.TryGetValue(idMax, out var maxStatsRaw)) continue;
            relById.TryGetValue(idMin, out var minStatsRaw);
            ratioByRank.TryGetValue(rank, out var ratio);

            var maxStats = (float[])maxStatsRaw.Clone();
            ApplyRatio(maxStats, ratio);
            float[] minStats = null;
            if (minStatsRaw != null)
            {
                minStats = (float[])minStatsRaw.Clone();
                ApplyRatio(minStats, ratio);
            }

            var attrs = new Dictionary<string, float>();
            for (int i = 1; i < maxStats.Length; i++)
            {
                if (Math.Abs(maxStats[i]) < 0.0001f) continue;
                attrs[EffName(i)] = (float)Math.Round(maxStats[i], 2);
            }
            var base1 = new Dictionary<string, float>();
            if (minStats != null)
            {
                for (int i = 1; i < minStats.Length; i++)
                {
                    if (Math.Abs(minStats[i]) < 0.0001f) continue;
                    base1[EffName(i)] = (float)Math.Round(minStats[i], 2);
                }
            }

            cardRows.Add(new
            {
                id = card.Id,
                name = T(card.NameAlias),
                cls = card.Class.ToString().Replace("CLASS_", ""),
                rank,
                maxLevel = maxLv,
                talent = card.Talent.ToString().Replace("CARD_TALENT_", ""),
                type = card.Type.ToString().Replace("CARD_TYPE_", ""),
                use = card.Use.ToString().Replace("CARD_USE_", ""),
                faction = card.Faction.ToString().Replace("CARD_FACTION_", ""),
                character = card.Character.ToString(),
                activeSkill = card.ActiveSkillId,
                passiveSkill = card.PassiveSkillId,
                maxStats = attrs,
                lv1Stats = base1
            });
        }

        File.WriteAllText(Path.Combine(dir, "cards.json"), JsonSerializer.Serialize(new
        {
            rankMaxLevel = rankMax,
            rankRatio = ratioByRank.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => new {
                hp = kv.Value.Hp, pAtk = kv.Value.P_Attack, fAtk = kv.Value.F_Attack,
                pDef = kv.Value.P_Defence, fDef = kv.Value.F_Defence,
                hit = kv.Value.Hit, avoid = kv.Value.Avoid, crit = kv.Value.Critical,
                critDef = kv.Value.CriticalDef, pen = kv.Value.Penetration, parry = kv.Value.Parry
            }),
            count = cardRows.Count,
            // 覆盖不到属性的卡单独统计，避免把「没数据」误当成「没这张卡」。
            missing = c.Cards.Where(x => { game::BSC_DEFINE.GetHighLowRank(x.Rank, out int hr, out int lr); int sr = hr > 0 ? game::BSC_DEFINE.CARD_MAX_LOW_RANK : lr; return !relById.ContainsKey(((int)x.Class << 22) + (sr << 12) + (rankMax.TryGetValue(x.Rank, out int m) ? m : 1)); })
                .GroupBy(x => new { x.Class, x.Rank, x.Type, x.Use })
                .Select(g => new { cls = g.Key.Class.ToString(), rank = g.Key.Rank, type = g.Key.Type.ToString(), use = g.Key.Use.ToString(), n = g.Count() })
                .OrderByDescending(x => x.n).ToArray(),
            cards = cardRows
        }, Json));

        // ---------- 2. 装备与宝石词条 ----------
        var equipRows = new List<object>();
        foreach (var it in c.Items)
        {
            if (it.ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT &&
                it.ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_GEM) continue;
            var attrs = new List<object>();
            for (int i = 0; i < it.AttributeValueIdType.Count && i < it.AttributeValueId.Count; i++)
            {
                attrs.Add(new { type = it.AttributeValueIdType[i].ToString().Replace("EQEFFECT_", ""), value = it.AttributeValueId[i] });
            }
            equipRows.Add(new
            {
                id = it.Id,
                name = T(it.NameAlias),
                kind = it.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_GEM ? "GEM" : it.ItemSubType.ToString().Replace("ITEM_SUB_TYPE_", ""),
                equipType = it.EquipmentType.ToString().Replace("EQUIPMENT_TYPE_", ""),
                grade = it.Grade.ToString().Replace("ITEM_GRADE_", ""),
                level = it.Level,
                cardId = it.CardId,
                exclusive = it.IsExclusiveItem(),
                attrs
            });
        }
        File.WriteAllText(Path.Combine(dir, "equipment.json"), JsonSerializer.Serialize(new
        {
            count = equipRows.Count,
            items = equipRows
        }, Json));

        // ---------- 3. 寻宝池 ----------
        var pools = c.GachaSets.Select(g => new
        {
            id = g.GachaCategoryIndex,
            name = T(g.NameAlias),
            grade = g.GachaGrade.ToString(),
            tab = g.GachaTabType.ToString(),
            count = g.GachaCount,
            costType = g.CostItemIdType.ToString().Replace("COST_TYPE_", ""),
            costId = g.CostItemId,
            costQty = g.CostItemQuantity,
            specialDropProb = g.SpecialDropProb,
            freeActivate = g.FreeActivate,
            freeCountMax = g.FreeCountMax,
            regenTime = g.RegenTime,
            accrueCount = g.AccrueCount,
            bonusRewardId = g.BonusRewardId,
            bonusRewardQty = g.BonusRewardQuantity,
            infos = g.GachaSetDataInfoList.Select(i => new
            {
                pcLvMin = i.PcLvMin,
                pcLvMax = i.PcLvMax,
                isFirst = i.IsFirst,
                slots = i.Slot,
                special = i.SpecialDrop,
                vip = i.VipLevel
            })
        }).ToList();
        File.WriteAllText(Path.Combine(dir, "gacha.json"), JsonSerializer.Serialize(new
        {
            count = pools.Count,
            pools
        }, Json));

        // ---------- 4. 技能 ----------
        var skillTable = ReadTable<BscTable.SkillDataTable>(root, "SkillDataTable");
        var skills = skillTable.List.Select(s => new
        {
            id = s.Id,
            name = T(s.NameAlias),
            desc = T(s.DescAlias),
            type = s.SkillType.ToString(),
            attackType = s.AttackType.ToString(),
            target = s.TargetType.ToString(),
            range = s.TargetRange.ToString(),
            position = s.TargetPosition.ToString(),
            final = s.FinalSkill,
            effects = s.SkillEffectId
        }).ToList();
        File.WriteAllText(Path.Combine(dir, "skills.json"), JsonSerializer.Serialize(new
        {
            count = skills.Count,
            skills
        }, Json));

        Console.WriteLine($"Balance audit: {cardRows.Count} cards, {equipRows.Count} equip/gem, {pools.Count} pools, {skills.Count} skills -> {dir}");
    }

    static T ReadTable<T>(string root, string name)
    {
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", name + ".pb"));
        return Serializer.Deserialize<T>(input);
    }
}
