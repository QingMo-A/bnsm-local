extern alias game;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;

internal static class ArtHelperCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string dynamic=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets");
        byte[] Decode(string name){var b=File.ReadAllBytes(Path.Combine(dynamic,name));return b[0]==0?b[1..]:game::Crypto.GetDecrypt(b[1..]);}
        byte[] blob=Decode("CDT.bytes");using var offsetsStream=new MemoryStream(Decode("CDT_off.bytes"));
        var offsets=Serializer.Deserialize<BscTable.DataOffsetTable>(offsetsStream).List;
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"HelperRewardDataTable","CardDataTable","CardSkinDataTable"}){
            var m=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            int id=m.GetProperty("id").GetInt32(),offset=m.GetProperty("offset").GetInt32(),length=m.GetProperty("length").GetInt32();
            byte[] b=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool equal=Enumerable.Range(0,offsets.Count/4).Any(i=>offsets[i*4]==id&&offsets[i*4+1]==offset&&offsets[i*4+2]==length)&&b.AsSpan().SequenceEqual(blob.AsSpan(offset,length));
            if(!equal)throw new InvalidDataException("Original table mismatch: "+name);
            sources.Add(new {name,sha256=Convert.ToHexString(SHA256.HashData(b)),matchesOriginalCdt=equal});
        }
        File.WriteAllText(Path.Combine(root,"evidence/art-helper-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",sources,
            localPolicy="Persist inclusive GetMin..GetMax threshold; normalize positive relative pool/reward weights; excess likes carried forward; duplicate characters become fragments",
            helpers=c.HelperRewards.Select(h=>new {h.Id,h.ItemId,h.GetMin,h.GetMax,h.Accumulation,pools=h.RewardPoolList.Select(p=>new {p.PoolId,p.PCLevelMin,p.PCLevelMax,p.PoolProb,p.RewardCount,rewards=p.RewardList.Select(r=>new {type=r.RewardIdType.ToString(),r.RewardId,r.Quantity,r.Prob})})}),
            art=c.UsableHeroes.Select(x=>new {x.Id,x.Rank,x.CardBig,x.CardSmall,special=x.SpecialCard.ToString()})
        },new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Artwork/helper audit: 3 tables match original CDT bytes");
    }
}
