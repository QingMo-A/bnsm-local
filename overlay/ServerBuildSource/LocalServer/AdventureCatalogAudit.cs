extern alias game;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;

internal static class AdventureCatalogAudit
{
    public static void Run(string root)
    {
        var c = new GameCatalog(root);
        var originalRoot = new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? root;
        string dynamic = Path.Combine(Directory.GetParent(originalRoot)!.FullName, "热更新包/files/UCUpdater/DynamicAssets");
        var source = new List<object>();
        string[] names = ["PieceDataTable", "CardDataTable", "FieldAchievementRateRewardTable_Normal", "FieldAchievementRateRewardTable_Hard", "FieldAchievementRateRewardTable_Legend",
            "DungeonStageTable_Normal","DungeonStageTable_Hard","DungeonStageTable_Legend",
            ..Directory.GetFiles(Path.Combine(root,"LocalServer/data/tables"),"DungeonStage_Field*.pb").Select(Path.GetFileNameWithoutExtension).OfType<string>()];
        if (File.Exists(Path.Combine(dynamic, "CDT.bytes"))) {
            byte[] Decode(string file) { var b=File.ReadAllBytes(Path.Combine(dynamic,file));return b[0]==0?b[1..]:game::Crypto.GetDecrypt(b[1..]); }
            byte[] blob=Decode("CDT.bytes");
            using var offsets=new MemoryStream(Decode("CDT_off.bytes"));
            var index=Serializer.Deserialize<BscTable.DataOffsetTable>(offsets).List;
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
            foreach (string name in names) {
                var row=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
                int offset=row.GetProperty("offset").GetInt32(),length=row.GetProperty("length").GetInt32(),id=row.GetProperty("id").GetInt32();
                bool indexed=Enumerable.Range(0,index.Count/4).Any(i=>index[i*4]==id&&index[i*4+1]==offset&&index[i*4+2]==length);
                var bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
                bool equal=indexed&&bytes.AsSpan().SequenceEqual(blob.AsSpan(offset,length));
                if(!equal)throw new InvalidDataException("Extracted table differs from original CDT: "+name);
                source.Add(new {name,offset,length,sha256=Convert.ToHexString(SHA256.HashData(bytes)),matchesOriginalCdt=equal});
            }
        }
        var report=new {
            version="1.97.0.0", originalCdtDirectory=dynamic, source,
            summonRule="BscMyCardDialog.OnPieceComplete -> PieceData.Complete / NextId; not CardRankData.NextEVP",
            pieces=c.Pieces.Select(x=>new {x.ItemId,x.Complete,nextType=x.NextIdType.ToString(),x.NextId,
                card=c.Cards.Where(y=>y.Id==x.NextId).Select(y=>new {y.NameAlias,y.Rank,y.Talent,y.PieceQuantity}).FirstOrDefault()}),
            fieldRewards=c.FieldRewards.SelectMany(d=>d.Value.Select(f=>new {difficulty=d.Key.ToString(),f.FieldId,rewards=f.List.Select((r,i)=>new {wireRewardId=i+1,r.Id,r.StarCount,r.RewardValueIdType,r.RewardValueId,r.Quantity})})),
            dungeons=c.Dungeons.Select(d=>new {difficulty=d.Key.ToString(),count=d.Value.Count,stages=d.Value.Values.Select(x=>new {x.Id,x.FieldId,x.OpenStageId,x.RequirePcLevel,x.ConsumeStamina,x.DailyLimit,x.Gold,x.RewardExp,x.CardRewardExp,waves=c.WaveIds(d.Key,(uint)x.Id,true)})}),
            unlock=new {GameCatalog.NormalFieldToUnlockHard,GameCatalog.HardFieldToUnlockLegend}
        };
        File.WriteAllText(Path.Combine(root,"evidence/adventure-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"Adventure catalog: {c.Pieces.Count} fragment recipes; {source.Count} tables matched original CDT.");
    }
}
