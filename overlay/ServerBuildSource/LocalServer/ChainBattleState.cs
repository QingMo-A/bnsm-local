internal sealed class LocalChainBattleState
{
    public ulong Day {get;set;}
    public uint Used {get;set;}
    public uint Tier {get;set;}
    public uint Step {get;set;}=1;
    public bool Continuing {get;set;}
    public uint Battles {get;set;}
    public uint Wins {get;set;}
    public uint BestWins {get;set;}
    public uint Buff {get;set;}
    public uint BuffRolls {get;set;}
    public byte[] Rewards {get;set;}=[];
    public byte[] Factions {get;set;}=[];
    public Dictionary<ulong,LocalWorldCard> Cards {get;set;}=[];
    public byte[] Enemy {get;set;}=[];
    public List<byte[]> EnemyCards {get;set;}=[];
    public List<byte[]> History {get;set;}=[];
    public HashSet<uint> FirstClears {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
