using System.Text.Json;

// Guilds live in their founder's player record, even after leadership transfer.
// A membership change and all affected players are one atomic players.json save.
internal sealed class LocalGuild
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public uint Id {get;set;}
    public string Name {get;set;}="";
    public string Greeting {get;set;}="";
    public uint Emblem {get;set;}
    public uint Border {get;set;}
    public GuildJoinType JoinType {get;set;}
    public uint MinLevel {get;set;}=17;
    public uint Level {get;set;}=1;
    public uint Points {get;set;}
    public ulong Created {get;set;}
    public uint Master {get;set;}
    public bool Disbanded {get;set;}
    public Dictionary<uint,LocalGuildMember> Members {get;set;}=[];
    public HashSet<uint> Applicants {get;set;}=[];
    public HashSet<uint> Invitees {get;set;}=[];
    public ulong AttendanceDay {get;set;}
    public uint YesterdayAttendance {get;set;}
    public ulong? TotalDonationPoints {get;set;}
    public uint DonationCount {get;set;}
    public HashSet<uint> ClaimedMissions {get;set;}=[];
    public List<LocalGuildHistory> History {get;set;}=[];
    public uint ChatSequence {get;set;}
    public List<string> Chat {get;set;}=[];
    public LocalGuildBattle Battle {get;set;}=new();
}
internal sealed class LocalGuildBattle
{
    public ulong Week {get;set;}
    public uint Battles {get;set;}
    public uint Wins {get;set;}
    public uint Points {get;set;}
    public uint BestBattles {get;set;}
    public uint BestWins {get;set;}
    public uint BestPoints {get;set;}
    public List<byte[]> History {get;set;}=[];
    public List<byte[]> ArenaHistory {get;set;}=[];
}
internal sealed class LocalGuildMember
{
    public bool IsBot {get;set;}
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public GuildUserGrade Grade {get;set;}=GuildUserGrade.GUILD_USER_GRADE_NORMAL;
    public ulong Joined {get;set;}
    public uint Contribution {get;set;}
    public uint WeeklyContribution {get;set;}
    public ulong Week {get;set;}
    public ulong AttendanceDay {get;set;}
}
internal sealed class LocalGuildHistory
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public GuildHistoryLogType Type {get;set;}
    public uint Data {get;set;}
    public uint Extra {get;set;}
    public ulong Time {get;set;}
    public string Name {get;set;}="";
    public string Target {get;set;}="";
}
internal sealed class LocalGuildAttendanceReward
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public uint GuildId {get;set;}
    public uint DataId {get;set;}
}
internal sealed class LocalGuildUser
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public uint GuildId {get;set;}
    public LatestMyGuildStatusFlag Latest {get;set;}
    public ulong WithdrawTime {get;set;}
    public ulong AttendanceDay {get;set;}
    public List<LocalGuildAttendanceReward> AttendanceRewards {get;set;}=[];
    public ulong Day {get;set;}
    public uint WorshipCount {get;set;}
    public HashSet<uint> Worshiped {get;set;}=[];
    public uint WorshipGold {get;set;}
    public uint ContributionTable {get;set;}
    public List<uint> Slots {get;set;}=[];
    public uint CurrentSlot {get;set;}
    public uint Resets {get;set;}
    public ulong BattleDay {get;set;}
    public HashSet<uint> BattleVolunteers {get;set;}=[];
}
