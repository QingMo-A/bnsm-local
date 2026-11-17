using System.Text.Json;

internal sealed class LocalRanking
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public bool Initialized {get;set;}
    public bool HasPower {get;set;}
    public uint TigerPower {get;set;}
    public uint DragonPower {get;set;}
    public uint SnakePower {get;set;}
    public uint ArenaPower {get;set;}
    public uint InfinitePower {get;set;}
    public ulong PowerUpdatedAt {get;set;}
    public ulong EngravingTotal {get;set;}
    public ulong ChargedTotal {get;set;}
    public ulong ChargeDay {get;set;}
    public ulong ChargedToday {get;set;}
    public uint WorldBest {get;set;}
    public uint ComboBest {get;set;}
    public ulong BigBossDamage {get;set;}
    public ulong LikeDay {get;set;}
    public HashSet<uint> Liked {get;set;}=[];
    public uint ReceivedToday {get;set;}
    public ulong ReceivedTotal {get;set;}
    public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
