extern alias game;
using ProtoBuf;
using System.Text.Json;
internal static class GuildAudit
{
 public static void Run(string root){
  T Read<T>(string name){using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
  var c=new GameCatalog(root);
  var data=new{
   items=c.Items.Where(x=>x.Id==270000001||x.Id==270000002),shop=c.Shops.Where(x=>x.ShopId==5),guildTokenOfferCount=c.Goods.Count(x=>x.GuildT>0),goods=c.Goods.Where(x=>x.GuildT>0).Take(12).Select(x=>new{x.GoodsId,x.ProductId,x.GuildT,x.Quantity,x.GuildLevelMin,x.GuildLevelMax,x.GuildPresent}),
   levels=Read<BscTable.GuildLevelDataTable>("GuildLevelDataTable").List,
   attendance=Read<BscTable.GuildAttendanceDataTable>("GuildAttendanceDataTable").List,
   donations=Read<BscTable.GuildContributionDataTable>("GuildContributionDataTable").List,
   worship=Read<BscTable.GuildWorshipDataTable>("GuildWorshipDataTable").List,
   emblems=Read<BscTable.GuildInsigniaDataTable>("GuildInsigniaDataTable").List,
   missions=Read<BscTable.MissionDataTable>("GuildMissionTable").List,
   numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("GUILD")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
   expenses=c.Expenses.List.Where(x=>x.ExpenseType.ToString().Contains("GUILD")||x.ExpenseType.ToString().Contains("CONTRIBUTION")),
   money=c.MoneyResources.Where(x=>x.MoneyEventType.ToString().Contains("GUILD")),
   contents=c.Contents.Where(x=>x.Alias.ToString().Contains("GUILD")),
   vip=c.Vip.Select(x=>new{x.VipLv,x.WorshipAvailableCount})
  };
  File.WriteAllText(Path.Combine(root,"evidence/guild-original-catalog.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
 }
}
