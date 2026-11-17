using System.Text.Json;

internal sealed class LocalCompetitiveState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public ulong Week {get;set;}
    public uint HomeMap {get;set;}
    public Dictionary<uint,LocalPvpRecord> Records {get;set;}=[];
    public Dictionary<string,LocalCastleSpot> Castles {get;set;}=[];
    public ulong CastleRaidSummonDay {get;set;}
    public List<byte[]> CastleHistory {get;set;}=[];
    public uint CastleAttacks {get;set;}
    public uint CastleSearches {get;set;}
    public Dictionary<uint,uint> CastleSkips {get;set;}=[];
    public uint CastleBought {get;set;}
    public uint CastlePaidLeft {get;set;}
    public uint CastleRecoveries {get;set;}
    public uint CastleExchangeBought {get;set;}
    public uint CastleExchangePaidLeft {get;set;}
    public uint CastleExchanges {get;set;}
    public uint CastleExchangeWealth {get;set;}
    public uint CastleRestoreHelps {get;set;}
    public HashSet<ulong> CastleDeadCards {get;set;}=[];
    public uint CastleTarget {get;set;}
    public LocalCompetitiveOffer? CastleOpponent {get;set;}
    public LocalCompetitiveProgress CastleDifficulty {get;set;}=new();
    public LocalCompetitiveProgress WorldDifficulty {get;set;}=new();
    public Dictionary<uint,List<LocalCompetitiveOffer>> Offers {get;set;}=[];
    public bool CastleStamped {get;set;}
    public bool CastleAdmissionPaid {get;set;}
    public HashSet<string> CastleTargetLooted {get;set;}=[];
    public uint CastleTargetAttacks {get;set;}
    public ulong WorldDay {get;set;}
    public ulong WorldWeek {get;set;}
    public Dictionary<uint,LocalWorldScore> WorldScores {get;set;}=[];
    public Dictionary<ulong,LocalWorldCard> WorldCards {get;set;}=[];
    public HashSet<uint> WantedBattled {get;set;}=[];
    public uint WorldRevives {get;set;}
    public uint WorldReviveBought {get;set;}
    public Dictionary<uint,uint> Wanted {get;set;}=[];
    public uint ActiveMode {get;set;}
    public uint Opponent {get;set;}
    public uint OpponentRank {get;set;}
    public uint Map {get;set;}
    public uint Spot {get;set;}
    public uint Action {get;set;}
    public ulong Stamp {get;set;}
    public ulong OpenedAt {get;set;}
    public Dictionary<ulong,byte[]> Replays {get;set;}=[];
    public bool Started {get;set;}
    public List<ulong> Cards {get;set;}=[];
    public List<ulong> ReserveCards {get;set;}=[];
    public bool ManualControl {get;set;}
    public uint GuildId {get;set;}
    public uint GuildMercenaryUid {get;set;}
    public List<uint> GuildReplayCards {get;set;}=[];
    public byte[] Enemy {get;set;}=[];
    public uint? ReplayWinner {get;set;}
    public List<uint> ReplayResults {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
internal sealed class LocalPvpRecord
{
    public uint LossStreak {get;set;}
    public uint CoolResets {get;set;}
    public uint Used {get;set;}
    public uint Bought {get;set;}
    public uint PaidLeft {get;set;}
    public uint Battles {get;set;}
    public uint Wins {get;set;}
    public uint Streak {get;set;}
    public uint MaxStreak {get;set;}
    public uint Points {get;set;}
    public uint Rank {get;set;}=10000;
    public uint BestRank {get;set;}=10000;
    public ulong Latest {get;set;}
    public uint RewardPoints {get;set;}
    public ulong RewardDay {get;set;}
    public HashSet<uint> RewardClaims {get;set;}=[];
    public List<byte[]> History {get;set;}=[];
}
internal sealed class LocalCastleSpot
{
    public uint Map {get;set;}
    public uint Spot {get;set;}
    public uint Status {get;set;}
    public bool Upgraded {get;set;}
    public uint Wealth {get;set;}
    public ulong WealthStamp {get;set;}
    public List<ulong> Defence {get;set;}=[];
    public uint Attacks {get;set;}
    public uint Defeats {get;set;}
    public ulong Attacked {get;set;}
    public uint Attacker {get;set;}
    public uint StoreReset {get;set;}
    public uint StoreRotation {get;set;}
    public List<uint> StoreGoods {get;set;}=[];
    public Dictionary<uint,uint> Purchases {get;set;}=[];
    public LocalCastleRaid? Raid {get;set;}
}
internal sealed class LocalWorldScore
{
    public uint Attack {get;set;}
    public uint Defence {get;set;}
    public uint Daily {get;set;}
    public uint Weekly {get;set;}
}
internal sealed class LocalWorldCard
{
    public float Hp {get;set;}=1;
    public float Sp {get;set;}
    public uint Bubbles {get;set;}
}
