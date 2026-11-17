using System.Text.Json;

internal static class DebugCatalogAudit
{
    public static void Run(string root)
    {
        var c = new GameCatalog(root);
        File.WriteAllText(Path.Combine(root,"evidence/debug-original-catalog.json"),JsonSerializer.Serialize(new {
            maxPlayer=ProgressionRules.PlayerMax(c),maxPlayerTable=c.PlayerStates.Max(x=>x.Level),
            goldItems=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GOLDITEM).Select(x=>new {x.Id,x.NameAlias,x.Sell,x.Value,money=x.SellMoneyType.ToString()}),
            vip=c.Vip.Select(x=>new {x.VipLv,x.LvUpNeedVipExp}),
            player=c.PlayerStates.Take(25).Select(x=>new {x.Level,x.Exp,x.MaxCardLevel}),
            card=c.CardLevels.Take(25).Select(x=>new {x.Level,x.Rank1})
        },new JsonSerializerOptions {WriteIndented=true}));
    }
}
