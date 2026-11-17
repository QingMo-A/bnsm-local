extern alias game;
using System.Text.Json;
using ProtoBuf;
internal static class TrainingCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string originalCdt=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets/CDT.bytes");
        byte[] packed=File.ReadAllBytes(originalCdt),cdt=packed[0]==0?packed[1..]:game::Crypto.GetDecrypt(packed[1..]);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"TrainingcampTable","TrainingcampExpTable","VIPDataTable"}) {
            var row=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            byte[] data=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool matches=data.AsSpan().SequenceEqual(cdt.AsSpan(row.GetProperty("offset").GetInt32(),row.GetProperty("length").GetInt32()));
            if(!matches)throw new InvalidDataException("Training source mismatch: "+name);
            sources.Add(new{name,matchesOriginalCdt=matches,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))});
        }
        var entry=manifest.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        using var textStream=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));
        var texts=Serializer.Deserialize<BscTable.LocalTextDataTable>(textStream);texts.MakeMap();
        File.WriteAllText(Path.Combine(root,"evidence/training-original-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",openLevel=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_TRAINING),
            sources,
            text=new[]{"Message.TrainingStop","Message.TrainingStopComplete","Message.FastTrainingStart","Message.Error.FastTraining","Message.Trainingcamp.FriendReward1"}.ToDictionary(x=>x,x=>texts.GetItem(x)?.Text),
            rules=c.Training,experience=c.TrainingExp,
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("TRAINING")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            slots=c.Vip.Select(x=>new{x.VipLv,x.TrainingBonusSlotCount})
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
