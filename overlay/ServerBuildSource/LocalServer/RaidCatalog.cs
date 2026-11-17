using ProtoBuf;

internal sealed class RaidCatalog
{
    public readonly List<BscTable.CardData> Private,World,Big,Friend;
    public readonly Dictionary<int,List<BscTable.WanderSpotData>> Wander=[];
    public readonly List<BscTable.WanderPointReward> WanderRewards;
    public readonly List<BscTable.PvpBuffData> Buffs;
    public readonly List<BscTable.PvpBuffSetData> BuffSets;
    public readonly List<BscTable.VIPPriceDiscountInfo> Prices;
    public readonly List<BscTable.WorldBossScheduleData> Schedules;
    public readonly List<BscTable.WorldBossRewardData> WorldRewards;
    public readonly List<BscTable.RaidRewardData> PrivateRewards;
    public readonly List<BscTable.BigBossRewardData> BigRewards;
    public readonly List<BscTable.BigBossStageLink> BigStages;
    public readonly List<BscTable.BigBossStageInfo> BigWaves;
    private readonly Dictionary<int,BscTable.CardRelativeStatusData> stats;
    private readonly Dictionary<int,BscTable.CardAttributeRankRatioData> ratios;
    public RaidCatalog(string root)
    {
        T Read<T>(string name){using var file=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(file);}
        foreach(var (id,suffix) in new[]{(1,"Normal"),(2,"Hard"),(3,"Hero")})Wander[id]=Read<BscTable.WanderSpotDataTable>("WanderSpotDataTable_"+suffix).List;
        WanderRewards=Read<BscTable.WanderPointRewardTable>("WanderPointRewardTable").List;
        BuffSets=Read<BscTable.PvpBuffSetDataTable>("PvpBuffSetDataTable").List;
        Buffs=Read<BscTable.PvpBuffDataTable>("PvpBuffDataTable").List;
        Prices=Read<BscTable.VIPPriceDiscountInfoTable>("VIPPriceDiscountTable").List;
        Private=Read<BscTable.CardDataTable>("RaidBossDataTable").List;
        Friend=Read<BscTable.CardDataTable>("FriendRaidBossDataTable").List;
        World=Read<BscTable.CardDataTable>("WorldBossDataTable").List;
        Big=Read<BscTable.CardDataTable>("BigBossDataTable").List;
        Schedules=Read<BscTable.WorldBossScheduleDataTable>("WorldBossScheduleTable").List;
        WorldRewards=Read<BscTable.WorldBossRewardDataTable>("WorldBossRewardDataTable").List;
        PrivateRewards=Read<BscTable.RaidRewardDataTable>("RaidRewardDataTable").List;
        BigRewards=Read<BscTable.BigBossRewardDataTable>("BigBossRewardDataTable").List;
        BigStages=Read<BscTable.BigBossStageLinkTable>("BigBossStageTable").List;
        BigWaves=Read<BscTable.BigBossStageInfoTable>("BigBossBattleField").List;
        stats=Read<BscTable.CardRelativeStatusDataTable>("CardRelativeStatusDataTable").List.ToDictionary(x=>x.Id);
        ratios=Read<BscTable.CardAttributeRankRatioDataTable>("CardAttributeRankRatioDataTable").List.ToDictionary(x=>x.Rank);
    }
    public uint Hp(BscTable.CardData card,uint level)
    {
        int rank=Math.Min(card.Rank,8); // BSC_DEFINE.CARD_MAX_LOW_RANK in client 1.97.
        int id=((int)card.Class<<22)+(rank<<12)+(int)level;
        float hp=0;
        if(stats.TryGetValue(id,out var row)&&row.status.Length>=8) {
            byte[] bytes=Enumerable.Range(0,4).Select(i=>(byte)(row.status[4+i]^BscTable.CardRelativeStatusDataTable.testtemp[i])).ToArray();hp=BitConverter.ToSingle(bytes);
        }
        if(ratios.TryGetValue(card.Rank,out var ratio))hp=(int)(hp*ratio.Hp);
        hp=(int)(hp+(hp*(card.StatHpMul/100f)+card.StatHpDiff));
        return (uint)Math.Clamp(hp,1,100000000);
    }
}
