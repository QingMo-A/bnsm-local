extern alias game;
using ProtoBuf;
using System.Text.Json;

/// <summary>
/// 只读导出：把七张 ItemDataTable 分表合并后的全部道具行连同中文本地化文本落盘，
/// 供分类清点使用。不修改任何存档与游戏状态。
/// </summary>
internal static class ItemNameExportAudit
{
    public static void Run(string root)
    {
        var c = new GameCatalog(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "LocalServer/data/tables/manifest.json")));
        var entry = manifest.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetInt32() == game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        string tableName = entry.GetProperty("name").GetString()!;
        using var input = File.OpenRead(Path.Combine(root, "LocalServer/data/tables", tableName + ".pb"));
        var texts = Serializer.Deserialize<BscTable.LocalTextDataTable>(input);
        texts.MakeMap();
        string Text(string alias) => string.IsNullOrEmpty(alias)
            ? string.Empty
            : (texts.GetItem(alias)?.Text ?? alias);

        var rows = c.Items.Select(x => new
        {
            x.Id,
            name = Text(x.NameAlias),
            x.NameAlias,
            type = x.ItemType.ToString(),
            sub = x.ItemSubType.ToString(),
            grade = x.Grade.ToString(),
            equip = x.EquipmentType.ToString(),
            card = x.CardIdAlias,
            // 专属判定走客户端原函数，避免自己重推条件：
            // 装备且（绑定卡 id / 角色类型 / 阵营 / 分组 任一非空）。
            exclusive = x.IsExclusiveItem(),
            cardId = x.CardId,
            characterType = x.CardCharacterType.ToString(),
            faction = x.CardFaction.ToString(),
            groupType = x.CardGroupType.ToString(),
            jobs = string.Join("/", x.RecommendJob),
            desc = Text(x.DescAlias)
        }).OrderBy(x => x.Id).ToList();

        // 七张表合并后可能撞 Id，单独点名，避免分类时把两条当一条。
        var duplicates = rows.GroupBy(x => x.Id).Where(g => g.Count() > 1)
            .Select(g => new { id = g.Key, names = g.Select(v => v.name).ToArray() }).ToArray();

        // 专属装备靠 CardId 绑定角色，把卡表的中文名一并导出，供 txt 里标注归属。
        var cards = c.Cards.Select(x => new
        {
            x.Id,
            name = Text(x.NameAlias),
            x.Rank,
            talent = x.Talent.ToString()
        }).OrderBy(x => x.Id).ToList();

        string path = Path.Combine(root, "evidence/item-names.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            textTable = tableName,
            total = rows.Count,
            distinctIds = rows.Select(x => x.Id).Distinct().Count(),
            duplicates,
            cards,
            items = rows
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        System.Console.WriteLine($"Item names exported: {rows.Count} rows, {rows.Select(x => x.Id).Distinct().Count()} ids -> {path}");
    }
}
