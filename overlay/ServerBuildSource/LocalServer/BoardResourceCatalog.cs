using ProtoBuf;
internal sealed class BoardResourceCatalog
{
    public readonly List<BscTable.BattleBoardData> Boards;
    public readonly List<BscTable.BoardDiceData> Dice;
    public readonly List<BscTable.ResourceStageLink> Resources;
    public readonly List<BscTable.ResourceStageRewardData>[] Rewards;
    public readonly List<BscTable.BattleStageInfo>[] Waves;
    public BoardResourceCatalog(string root)
    {
        T Read<T>(string name) {string p=Path.Combine(root,"LocalServer/data/tables",name+".pb");if(!File.Exists(p))p=Path.Combine(root,"LocalServer/data/tables",$"hash_{Hash(name):X8}.pb");using var f=File.OpenRead(p);return Serializer.Deserialize<T>(f);}
        Boards=Read<BscTable.BattleBoardDataTable>("BattleBoardDataTable").List;
        Dice=Read<BscTable.BoardDiceDataTable>("BoardDiceDataTable").List;
        Resources=Read<BscTable.ResourceStageLinkTable>("ResourceStageTable").List;
        Rewards=Enumerable.Range(0,6).Select(i=>Read<BscTable.ResourceStageRewardDataTable>("ResourceStageRewardDataTable"+i).List).ToArray();
        Waves=Enumerable.Range(0,6).Select(i=>Read<BscTable.BattleStageInfoTable>("ResourceStage"+i).List).ToArray();
        foreach(var map in Boards){var ids=map.SpotList.Select(x=>x.SpotId).ToHashSet();if(ids.Count!=map.SpotList.Count||map.SpotList.Any(x=>x.FirstNextSpotId!=0&&!ids.Contains(x.FirstNextSpotId)||x.SecondNextSpotId!=0&&!ids.Contains(x.SecondNextSpotId)))throw new InvalidDataException("Invalid original board graph");}
    }
    // Same original Global table-name hash; hashed CDT files remain byte-for-byte unchanged.
    private static int Hash(string text) {unchecked {int a=352654597,b=a;foreach(char c in text){a=((a<<5)+a+(a>>27))^(c>>8);b=((b<<5)+b+(b>>27))^(c&255);}return a+b*1566083941;}}
}
