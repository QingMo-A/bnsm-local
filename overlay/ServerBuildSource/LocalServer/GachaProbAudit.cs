extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// 只读寻宝概率审计：导出 VIP 寻宝的日/周轮换表（带真实 Prob 权重）、
/// GachaSetData 的槽位与保底标记。这是客户端资源里唯一带概率的寻宝数据。
/// </summary>
internal static class GachaProbAudit
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

        // GachaDataTable / GachaDataInfoTable / GachaGroupDataTable / VIPGacha*ScheduleTable
        // 在 5.3 客户端资源里全部缺失 —— 客户端只保留 GachaSetDataTable（花费+展示槽位号），
        // 真实掉落与概率完全由服务端决定。

        // ---------- 2. 池子槽位明细（含 SpecialDrop / VipLevel 标记） ----------
        var set = ReadTable<BscTable.GachaSetDataTable>(root, "GachaSetDataTable");
        var setRows = set.List.Select(p => new
        {
            id = p.GachaCategoryIndex,
            name = T(p.NameAlias),
            desc = T(p.DescAlias),
            grade = p.GachaGrade.ToString(),
            tab = p.GachaTabType.ToString(),
            count = p.GachaCount,
            costType = p.CostItemIdType.ToString(),
            costId = p.CostItemId,
            specialDropProb = p.SpecialDropProb,
            free = p.FreeActivate,
            freeTicketType = p.CostFreeTicketItemIdType.ToString(),
            bonusRewardType = p.BonusRewardIdType.ToString(),
            bonusRewardAlias = p.BonusRewardIdAlias,
            bonusRewardDesc = T(p.BonusRewardDescAlias),
            infos = p.GachaSetDataInfoList.Select(i => new
            {
                pcLvMin = i.PcLvMin,
                pcLvMax = i.PcLvMax,
                isFirst = i.IsFirst,
                slots = i.Slot,
                specialDrop = i.SpecialDrop,
                vipLevel = i.VipLevel
            }).ToList()
        }).ToList();
        File.WriteAllText(Path.Combine(dir, "gacha-sets-full.json"),
            JsonSerializer.Serialize(new { count = setRows.Count, sets = setRows }, Json));

        // VIPGachaSetDataTable.pb 仍在包里，但 5.3 客户端已无对应类 → 视为废弃表，不导出。

        Console.WriteLine($"Gacha prob audit: {setRows.Count} sets -> {dir}");
    }

    static T ReadTable<T>(string root, string name)
    {
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", name + ".pb"));
        return Serializer.Deserialize<T>(input);
    }
}
