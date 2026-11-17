using ProtoBuf;
using System.Text.Json;
internal static class MissionAudit
{
    public static void Run(string root)
    {
        T Read<T>(string name) { using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f); }
        var rows=Read<BscTable.MissionDataTable>("MissionTable").List;
        var c=new GameCatalog(root);var usable=c.UsableHeroes.Select(x=>x.Id).ToHashSet();
        var output=new { missions=rows, guildMissions=Read<BscTable.MissionDataTable>("GuildMissionTable").List,
            unavailableCardRewards=rows.SelectMany(x=>x.RewardId.Select((id,i)=>new{x.MissionId,id,type=x.RewardIdType[i]})).Where(x=>x.type==BscTable.REWARD_TYPE.REWARD_TYPE_CARD&&!usable.Contains(x.id)),
            missingItemRewards=rows.SelectMany(x=>x.RewardId.Select((id,i)=>new{x.MissionId,id,type=x.RewardIdType[i]})).Where(x=>x.type==BscTable.REWARD_TYPE.REWARD_TYPE_ITEM&&!c.ItemsById.ContainsKey(x.id)) };
        File.WriteAllText(Path.Combine(root,"evidence/mission-original-catalog.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
        foreach(var g in rows.GroupBy(x=>x.MissionType)) Console.WriteLine(g.Key+": "+g.Count());
    }
}
