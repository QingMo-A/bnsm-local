using ProtoBuf;

internal sealed class CompetitiveCatalog
{
    public readonly List<BscTable.PvpData> Modes;
    public readonly List<BscTable.ChainBattleData> ChainBattles;
    public readonly List<BscTable.PvpRewardData> Rewards;
    public readonly List<BscTable.CastleMapData> CastleMaps;
    public readonly List<BscTable.CastleData> Castles;
    public readonly List<BscTable.WorldWarMapData> Territories;
    public readonly List<BscTable.WorldWarRewardData> WorldRewards;
    public readonly List<BscTable.ItemEffectData> ItemEffects;
    public readonly List<BscTable.BalanceCardPvpData> BalanceCards;
    public readonly Dictionary<BscTable.GAME_MODE,BscTable.BattleLogicConfig> BattleRules;
    public CompetitiveCatalog(string root)
    {
        T Read<T>(string name){using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
        Modes=Read<BscTable.PvpDataTable>("PvpDataTable").List;
        ChainBattles=Read<BscTable.ChainBattleDataTable>("ChainBattleDataTable").List;
        Rewards=Read<BscTable.PvpRewardDataTable>("PvpRewardDataTable").List;
        CastleMaps=Read<BscTable.CastleMapDataTable>("CastleMapDataTable").List;
        Castles=Read<BscTable.CastleDataTable>("CastleDataTable").List;
        Territories=Read<BscTable.WorldWarMapDataTable>("WorldWarMapDataTable").List;
        WorldRewards=Read<BscTable.WorldWarRewardDataTable>("WorldWarRewardDataTable").List;
        ItemEffects=Read<BscTable.ItemEffectDataTable>("ItemEffectDataTable").List;
        BalanceCards=Read<BscTable.BalanceCardPvpDataTable>("BalanceCardPvpDataTable").List;
        foreach (var entry in LocalCardRegistry.Entries)
        {
            var row = entry.CreateBalance(BalanceCards.Single(x => x.CardId == entry.BalanceTemplateId));
            if (BalanceCards.Any(x => x.CardId == row.CardId)) throw new InvalidDataException($"Registered balance card collision: {entry.Name}");
            BalanceCards.Add(row);
        }
        BattleRules=Read<BscTable.BattleLogicConfigTable>("BattleLogicConfig").List.ToDictionary(x=>x.GameMode);
    }
}
