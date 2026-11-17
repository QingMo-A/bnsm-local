extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Security.Cryptography;

internal static class CardAvailabilityAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var entry=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        using var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));
        var texts=Serializer.Deserialize<BscTable.LocalTextDataTable>(input);texts.MakeMap();
        string Text(string alias)=>texts.GetItem(alias)?.Text??alias;
        bool Match(string alias)=>new[]{"痞子盗","苏玲珑","龙丸"}.Any(x=>Text(alias).Contains(x));
        var cards=c.Cards.Where(x=>Match(x.NameAlias)).ToList();
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        byte[] packed=File.ReadAllBytes(Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets/CDT.bytes"));
        byte[] cdt=packed[0]==0?packed[1..]:game::Crypto.GetDecrypt(packed[1..]);
        var sources=new List<object>();
        foreach(string name in new[]{"CardDataTable","PieceDataTable","NpcDataTable",entry.GetProperty("name").GetString()!}) {
            var row=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            byte[] bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            if(!bytes.AsSpan().SequenceEqual(cdt.AsSpan(row.GetProperty("offset").GetInt32(),row.GetProperty("length").GetInt32())))throw new InvalidDataException(name);
            sources.Add(new{name,matchesOriginalCdt=true,sha256=Convert.ToHexString(SHA256.HashData(bytes))});
        }
        File.WriteAllText(Path.Combine(root,"evidence/card-availability-original.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",sources,
            cards=cards.Select(x=>new{x.Id,name=Text(x.NameAlias),x.NameAlias,x.Rank,x.MaxRank,x.CollectionId,use=x.Use.ToString(),type=x.Type.ToString(),talent=x.Talent.ToString(),x.EvolveFirstAncestor,x.RankUpCardId,x.PieceId,x.PieceQuantity,x.CardMiniAtlas,x.CardMini,x.CardSmall,x.CardBig,x.Minimi,x.Character,collectible=CardIdentityPolicy.IsCollectible(x),usable=c.UsableHeroes.Any(v=>v.Id==x.Id),recipe=c.Pieces.Where(v=>v.ItemId==x.PieceId),inGoldPool=c.DrawPool(false,4).Any(v=>v.Id==x.Id),inDiamondPool=c.DrawPool(true,4).Any(v=>v.Id==x.Id)}),
            fragments=c.Items.Where(x=>Match(x.NameAlias)).Select(x=>new{x.Id,name=Text(x.NameAlias),x.ItemType,x.ItemSubType})
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
