extern alias game;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;

internal static class LootCatalogAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);
        string original=new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName??root;
        string dynamic=Path.Combine(Directory.GetParent(original)!.FullName,"热更新包/files/UCUpdater/DynamicAssets");
        byte[] Decode(string file) {var b=File.ReadAllBytes(Path.Combine(dynamic,file));return b[0]==0?b[1..]:game::Crypto.GetDecrypt(b[1..]);}
        byte[] blob=Decode("CDT.bytes");using var offsetStream=new MemoryStream(Decode("CDT_off.bytes"));
        var offsets=Serializer.Deserialize<BscTable.DataOffsetTable>(offsetStream).List;
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var sources=new List<object>();
        foreach(string name in new[]{"BoxDataTable","DropGroupDataTable","BattleStageTable_Normal","BattleStageTable_Hard","BattleStageTable_Legend","DungeonStageTable_Hard","NumberTable","VIPDataTable"}) {
            var m=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("name").GetString()==name);
            int offset=m.GetProperty("offset").GetInt32(),length=m.GetProperty("length").GetInt32(),id=m.GetProperty("id").GetInt32();
            var bytes=File.ReadAllBytes(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            bool equal=Enumerable.Range(0,offsets.Count/4).Any(i=>offsets[i*4]==id&&offsets[i*4+1]==offset&&offsets[i*4+2]==length)&&bytes.AsSpan().SequenceEqual(blob.AsSpan(offset,length));
            if(!equal)throw new InvalidDataException("Original loot table mismatch: "+name);
            sources.Add(new {name,offset,length,sha256=Convert.ToHexString(SHA256.HashData(bytes)),matchesOriginalCdt=equal});
        }
        object Stage(BscTable.BattleStageLink x)=>new {x.Id,x.FieldId,x.ConsumeStamina,x.RewardExp,x.CardRewardExp,x.Gold,x.DailyLimit,x.NpcRewardId,x.NpcRewardIdType,x.RewardQuantity,x.DropProb,x.FirstNpcRewardId,x.FirstNpcRewardIdType,x.FirstRewardQuantity,x.FirstDropProb};
        var report=new {version="1.97.0.0",sources,numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("SKIP")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),vip=c.Vip.Select(x=>new {x.VipLv,x.ChainBattleSkip}),
            unavailableBoxRewards=c.Boxes.SelectMany(b=>ProtocolRouter.BoxColumns(b).SelectMany(x=>x).Where(x=>x.Card?!c.UsableHeroes.Any(i=>i.Id==x.Id):!c.Items.Any(i=>i.Id==x.Id)).Select(x=>new {box=b.Id,x.Card,x.Id})).Distinct(),
            boxCounts=new {total=c.Boxes.Count,fixedBoxes=c.Boxes.Count(x=>!x.GiveRandom),randomBoxes=c.Boxes.Count(x=>x.GiveRandom)},
            boxes=c.Boxes.Where(x=>new[]{205080063,205080065,205080072,205060318,205070451}.Contains(x.Id)).Select(x=>new {x.Id,x.UnlockId,x.GiveRandom,x.UseRewardSlot,x.GoldMin,x.GoldMax,x.CashMin,x.CashMax,x.MoonstoneMin,x.MoonstoneMax,x.CardMin,x.CardMax,x.ItemMin,x.ItemMax,
                name=c.Items.FirstOrDefault(i=>i.Id==x.Id)?.NameAlias,
                rows=x.BoxDataInfoList.Select(r=>new {cards=new[]{r.Card1Id,r.Card2Id,r.Card3Id,r.Card4Id},cardWeights=new[]{r.Card1Prob,r.Card2Prob,r.Card3Prob,r.Card4Prob},items=new[]{r.Item1Id,r.Item2Id,r.Item3Id,r.Item4Id},quantities=new[]{r.Item1Quantity,r.Item2Quantity,r.Item3Quantity,r.Item4Quantity},weights=new[]{r.Item1Prob,r.Item2Prob,r.Item3Prob,r.Item4Prob},r.CardId,r.CardProb,r.ItemId,r.ItemQuantity,r.ItemProb})}),
            stageProbabilityColumns=new[]{c.Stages.Values,c.HardStages.Values,c.LegendStages.Values,c.Dungeons.Values.SelectMany(x=>x.Values)}.Select(rows=>new {count=rows.Count(),withProbability=rows.Count(x=>x.DropProb.Count>0),withFirstProbability=rows.Count(x=>x.FirstDropProb.Count>0)}),
            normalExamples=c.Stages.Values.Where(x=>x.Id is 13 or 14 or 17 or 18 or 19).Select(Stage),
            groupCount=c.DropGroups.Count,groupsWithProbability=c.DropGroups.Count(x=>x.DropProb.Count>0),
            groupExamples=c.DropGroups.Take(2).Select(x=>new {x.Id,type=x.ProbCalculationType.ToString(),x.NpcRewardIdType,x.NpcRewardId,x.Quantity,x.DropProb})};
        File.WriteAllText(Path.Combine(root,"evidence/loot-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"Loot audit: {sources.Count} original CDT tables; {c.Boxes.Count} boxes, {c.DropGroups.Count} drop groups");
    }
}
