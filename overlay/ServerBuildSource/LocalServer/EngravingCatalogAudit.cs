extern alias game;
using System.Text.Json;
using System.Security.Cryptography;
using ProtoBuf;

internal static class EngravingCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string dynamic=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets");
        byte[] Decode(string file) {var b=File.ReadAllBytes(Path.Combine(dynamic,file));return b[0]==0?b[1..]:game::Crypto.GetDecrypt(b[1..]);}
        byte[] blob=Decode("CDT.bytes");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"EquipmentDataTable","EmpowerItemDataTable","NumberTable","ItemCardProcessData","ContentDataTable"}) {
            var m=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            int offset=m.GetProperty("offset").GetInt32(),length=m.GetProperty("length").GetInt32();
            var bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            if(!bytes.AsSpan().SequenceEqual(blob.AsSpan(offset,length)))throw new InvalidDataException(name);
            sources.Add(new {name,sha256=Convert.ToHexString(SHA256.HashData(bytes)),matchesOriginalCdt=true});
        }
        var entry=manifest.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        using var ts=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));
        var text=Serializer.Deserialize<BscTable.LocalTextDataTable>(ts).List;
        var report=new {version="1.97.0.0",sources,
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("EMPOWER")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            contents=c.Contents.Where(x=>x.Alias.ToString().Contains("EMPOWER")).Select(x=>new {type=x.Alias.ToString(),value=c.ContentValue(x.Alias)}),
            processes=c.CardProcesses.List.Where(x=>x.ProcessType.ToString().Contains("EXTRACT")||x.ProcessType.ToString().Contains("EMPOWER")),
            equipment=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).GroupBy(x=>x.Rank).Select(g=>new {rank=g.Key,count=g.Count(),minCap=g.Min(x=>x.EmpowerMaxCost),maxCap=g.Max(x=>x.EmpowerMaxCost),examples=g.Take(2).Select(x=>new {x.Id,x.NameAlias,x.Step,x.Level,x.Extraction,x.Empower,x.EmpowerMaxCost,x.AttributeValueIdType,x.AttributeValueId,x.AttributeUnit})}),
            stones=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE).Select(x=>new {x.Id,x.NameAlias,x.Rank,x.Step,x.Level,x.Grade,x.EmpowerFlag,x.EmpowerCost,x.EmpowerLimitItemLevel,x.EmpowerValue,x.AttributeValueIdType,x.AttributeValueId,x.Next1Id,x.PrevId,x.Material1Id,x.Material1Value}),
            texts=text.Where(x=>x.Text!=null&&(x.Text.Contains("刻印")||x.Text.Contains("分解")))};
        File.WriteAllText(Path.Combine(root,"evidence/engraving-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Engraving original tables verified: "+sources.Count);
    }
}
