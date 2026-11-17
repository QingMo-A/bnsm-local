using ProtoBuf;
using System.Text.Json;
internal static class BoardFactionAudit
{
    internal static int TableHash(string text) {unchecked {int a=352654597,b=a;foreach(char c in text){a=((a<<5)+a+(a>>27))^(c>>8);b=((b<<5)+b+(b>>27))^(c&255);}return a+b*1566083941;}}
    public static void Run(string root)
    {
        T Read<T>(string name) {string p=Path.Combine(root,"LocalServer/data/tables",name+".pb");if(!File.Exists(p))p=Path.Combine(root,"LocalServer/data/tables",$"hash_{TableHash(name):X8}.pb");using var f=File.OpenRead(p);return Serializer.Deserialize<T>(f);}
        var c=new GameCatalog(root);
        var output=new {boards=Read<BscTable.BattleBoardDataTable>("BattleBoardDataTable").List,dice=Read<BscTable.BoardDiceDataTable>("BoardDiceDataTable").List,
            resources=Read<BscTable.ResourceStageLinkTable>("ResourceStageTable").List,
            resourceRewards=Enumerable.Range(0,6).Select(i=>new {difficulty=i,rows=Read<BscTable.ResourceStageRewardDataTable>("ResourceStageRewardDataTable"+i).List}),
            resourceWaves=Enumerable.Range(0,6).Select(i=>new {difficulty=i,rows=Read<BscTable.BattleStageInfoTable>("ResourceStage"+i).List}),
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("BATTLE_BOARD")||x.Key.ToString().Contains("RESOURCE")),
            gates=c.Contents.Where(x=>x.Alias.ToString().Contains("BATTLE_BOARD")||x.Alias.ToString().Contains("DUNGEON_ELEMENT")),
            prices=Read<BscTable.VIPPriceDiscountInfoTable>("VIPPriceDiscountTable").List.Where(x=>x.VipRiceDiscountType==BscTable.VIP_PRICE_DISCOUNT_TYPE.VIP_PRICE_DISCOUNT_BUY_BATTLE_BOARD_DICE_POINT),
            expenses=c.Expenses.List.Where(x=>x.ExpenseType.ToString().Contains("BATTLE_BOARD")),vip=c.Vip.Take(3),money=c.MoneyResources.Where(x=>x.MoneyEventType.ToString().Contains("RESOURCE"))};
        File.WriteAllText(Path.Combine(root,"evidence/board-faction-original-catalog.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
    }
}
