extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Security.Cryptography;

internal static class UpgradeTowerCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        T Read<T>(string name){using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        byte[] packed=File.ReadAllBytes(Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets/CDT.bytes"));
        byte[] cdt=packed[0]==0?packed[1..]:game::Crypto.GetDecrypt(packed[1..]);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"ChallengeStageTable","ChallengeStageField","ChallengeTowerTable","ChallengeTowerBanGroupTable","ChallengePointRewardTable","RecipeDataTable"}) {
            var m=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            byte[] data=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            if(!data.AsSpan().SequenceEqual(cdt.AsSpan(m.GetProperty("offset").GetInt32(),m.GetProperty("length").GetInt32())))throw new InvalidDataException(name);
            sources.Add(new{name,matchesOriginalCdt=true,sha256=Convert.ToHexString(SHA256.HashData(data))});
        }
        var entry=manifest.RootElement.EnumerateArray().First(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        var texts=Read<BscTable.LocalTextDataTable>(entry.GetProperty("name").GetString()!);
        File.WriteAllText(Path.Combine(root,"evidence/upgrade-tower-original-catalog.json"),JsonSerializer.Serialize(new {
            version="1.97.0.0",sources,numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("CHALLENGE_TOWER")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            contents=c.Contents.Where(x=>x.Alias.ToString().Contains("CHALLENGE_TOWER")).Select(x=>new{type=x.Alias.ToString(),value=c.ContentValue(x.Alias)}),
            towers=Read<BscTable.ChallengeTowerDataTable>("ChallengeTowerTable").List,
            stages=Read<BscTable.ChallengeStageLinkTable>("ChallengeStageTable").List,
            waves=Read<BscTable.BattleStageInfoTable>("ChallengeStageField").List.Select(x=>new{x.Id,waves=x.WaveList.Count}),
            bans=Read<BscTable.ChallengeTowerBanGroupDataTable>("ChallengeTowerBanGroupTable").List,
            pointRewards=Read<BscTable.ChallengePointRewardDataTable>("ChallengePointRewardTable").List,
            recipes=c.Recipes.Where(x=>c.Items.Any(i=>i.Id==x.TargetItemId&&i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT)).Select(x=>new{x.TargetItemId,x.TargetItemLevel,x.TargetItemQuantity,x.Cost,x.CostType,x.MeterialItemId,x.MeterialItemQuantity}),
            texts=texts.List.Where(x=>x.Text!=null&&(x.Text.Contains("挑战塔")||x.Text.Contains("每周一")||x.Text.Contains("每周日")))
        },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
