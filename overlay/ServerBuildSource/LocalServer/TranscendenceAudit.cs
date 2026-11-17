using System.Text.Json;
internal static class TranscendenceAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        File.WriteAllText(Path.Combine(root,"evidence/transcendence-original-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",openLevel=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_TRANSCENDENCE),
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("TRANSCEND")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            count=c.Transcendence.Count,sample=c.Transcendence.Take(2),
            cards=c.Transcendence.Select(t=>c.Cards.FirstOrDefault(x=>x.Id==t.CardId)).Where(x=>x!=null).Select(x=>new{x!.Id,x.Rank,x.EvolveFirstAncestor}),
            effectKinds=c.Transcendence.SelectMany(x=>x.EffectDataList).GroupBy(x=>x.EffectType).Select(x=>new{type=x.Key.ToString(),count=x.Count()}),
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
