using System.Text.Json;
internal static class PolishAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        File.WriteAllText(Path.Combine(root,"evidence/polish-original-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",
            open=new {reset=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_RESET),formation=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_FORMATION),relic=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_RELIC_ENCHANT)},
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("CARD_RESET")||x.Key.ToString().Contains("FORMATION")||x.Key.ToString().Contains("RELIC")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            formations=c.Formations,expenses=c.FormationExpenses,
            relics=c.Relics.Select(x=>new{x.Class,levels=x.InfoList.Count,first=x.InfoList.Take(3),last=x.InfoList.Last()}),
            reset=c.CardResets,resetTop=c.CardResets.Last(),resetRewards=c.CardResetRewards,
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
