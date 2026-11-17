using ProtoBuf;

internal sealed class GuildCatalog
{
    public readonly List<BscTable.GuildLevelData> Levels;
    public readonly List<BscTable.GuildAttendanceData> Attendance;
    public readonly List<BscTable.GuildContributionData> Contributions;
    public readonly List<BscTable.GuildWorshipData> Worship;
    public readonly List<BscTable.GuildInsigniaData> Emblems;
    public readonly List<BscTable.MissionData> Missions;
    public GuildCatalog(string root)
    {
        T Read<T>(string name){using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
        Levels=Read<BscTable.GuildLevelDataTable>("GuildLevelDataTable").List;
        Attendance=Read<BscTable.GuildAttendanceDataTable>("GuildAttendanceDataTable").List;
        Contributions=Read<BscTable.GuildContributionDataTable>("GuildContributionDataTable").List;
        Worship=Read<BscTable.GuildWorshipDataTable>("GuildWorshipDataTable").List;
        Emblems=Read<BscTable.GuildInsigniaDataTable>("GuildInsigniaDataTable").List;
        Missions=Read<BscTable.MissionDataTable>("GuildMissionTable").List;
    }
}
