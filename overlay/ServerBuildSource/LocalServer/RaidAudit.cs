extern alias game;
using System.Text.Json;
using ProtoBuf;
internal static class RaidAudit
{
    public static void Run(string root)
    {
        var c=new GameCatalog(root);var r=new RaidCatalog(root);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        var entry=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
        using var file=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));var text=Serializer.Deserialize<BscTable.LocalTextDataTable>(file);text.MakeMap();
        string Label(string alias)=>text.GetItem(alias)?.Text??alias;
        var labels=new[]{"Label.Manor.RaidBoss","Label.Button.WorldBossShortcut","Label.Button.WorldBossComboShortcut","Label.BigBoss","Message.RaidGate.RaidBoss","Message.RaidGate.WorldBossShortcut","Message.RaidGate.WorldBossComboShortcut","Message.RaidGate.BigBoss"}.ToDictionary(x=>x,Label);
        object Cards(List<BscTable.CardData> rows)=>rows.Select(x=>new{x.Id,name=Label(x.NameAlias),x.Rank,x.Level,x.Class,x.BossGrade,x.StatHpDiff,x.StatHpMul,hp= r.Hp(x,(uint)Math.Max(1,x.Level))});
        var result=new{labels, textMatches=text.List.Where(x=>x.Text!=null&&(x.Text.Contains("ÑýÄ§")||x.Text.Contains("´³µ´½­ºþ"))).Select(x=>new{x.Id,x.Text}),r.Wander,r.WanderRewards,buffs=r.Buffs.Select(x=>x.Id),r.Prices,privateBosses=Cards(r.Private),worldBosses=Cards(r.World),bigBosses=Cards(r.Big),r.Schedules,r.WorldRewards,r.PrivateRewards,r.BigRewards,r.BigStages,r.BigWaves,
            privateUnlocks=c.HardStageRows.Where(x=>x.GenRaidMobId!=0).Select(x=>new{x.Id,x.GenRaidMobId}),
            buffSets=r.BuffSets,
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("BOSS")||x.Key.ToString().Contains("RAID")||x.Key.ToString().Contains("WANDER")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            contents=c.Contents.Where(x=>x.Alias.ToString().Contains("BOSS")||x.Alias.ToString().Contains("RAID")||x.Alias==BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_DUNGEON_EVIL),
            vip=c.Vip.Select(x=>new{x.VipLv,x.RaidBossEnterCount,x.WorldBossEnterCount,x.ComboBossEnterCount,x.BigBossEnterCount}),
            expenses=c.Expenses.List.Where(x=>x.ExpenseType.ToString().Contains("BOSS")||x.ExpenseType.ToString().Contains("RAID"))};
        File.WriteAllText(Path.Combine(root,"evidence/raid-original-catalog.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine($"Raid catalog: private={r.Private.Count}, world={r.World.Count}, big={r.Big.Count}; original rewards={r.PrivateRewards.Count}/{r.WorldRewards.Count}/{r.BigRewards.Count}");
        foreach(var pair in labels)Console.WriteLine(pair.Key+" = "+pair.Value);
    }
}
