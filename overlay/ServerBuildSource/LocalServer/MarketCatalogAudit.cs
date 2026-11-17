extern alias game;
using System.Text.Json;
internal static class MarketCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        var wanted=c.Shops.Take(2).SelectMany(x=>x.GoodsIdList).SelectMany(x=>x.GoodsId).ToHashSet();
        var probes=new List<object>();
        foreach(string file in Directory.EnumerateFiles(Path.Combine(root,"LocalServer/data/tables"),"*.pb")) {
            try {using var stream=File.OpenRead(file);var groups=ProtoBuf.Serializer.Deserialize<BscTable.GoodsGroupDataTable>(stream).List;var matches=groups.Where(x=>wanted.Contains(x.GoodsGroupId)&&x.GoodsGroupList.Count>0).ToList();if(matches.Count>0)probes.Add(new {file=Path.GetFileName(file),hits=matches.Count,rows=matches});} catch(Exception) {}
        }
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string dynamic=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets");
        var encoded=File.ReadAllBytes(Path.Combine(dynamic,"CDT.bytes"));var blob=encoded[0]==0?encoded[1..]:game::Crypto.GetDecrypt(encoded[1..]);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"ShopSetDataTable","GoodsDataTable","ExpenseWeightDataTable","VIPDataTable","ContentDataTable"}){
            var entry=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            int offset=entry.GetProperty("offset").GetInt32(),length=entry.GetProperty("length").GetInt32();
            byte[] bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool equal=bytes.AsSpan().SequenceEqual(blob.AsSpan(offset,length));if(!equal)throw new InvalidDataException("Original market table mismatch: "+name);
            sources.Add(new{name,matchesOriginalCdt=equal,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))});
        }
        var data=new {version="1.97.0.0",sources,groupCandidates=probes,groupTablesMatched=probes.Count,fallback="Missing server GoodsGroup membership/weights: eligible original item offers sampled equally without replacement; one pack per slot/refresh. Prices, quantities, tier slot count, VIP gates and refresh costs/period are original. Boss catalogue and prices are explicitly local.",rest=new {shops=c.Shops.Take(2),originalGoodsCount=c.Goods.Count,goodsSample=c.Goods.Take(10),refresh=c.Expenses.List.Where(x=>x.ExpenseType==BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_MARKET_SHOP),vip=c.Vip.Select(x=>new{x.VipLv,x.ShopGoodsMaxResetCount})}};
        File.WriteAllText(Path.Combine(root,"evidence/market-original-catalog.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
