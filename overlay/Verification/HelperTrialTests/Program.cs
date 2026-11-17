extern alias game;
using System.Text.Json;

string root="I:/BnsmModernWork/WindowsPreview";
if(args.Contains("--test")){await HelperCostumeRegression.Run(root);return;}
if(args.Contains("--audit")) {
    var c=new GameCatalog(root);
    Console.WriteLine(JsonSerializer.Serialize(new{chain=c.Items.Where(x=>LocalHelperCostumeDefinition.IsItem(x.Id)).Select(x=>new{x.Id,x.EnchantValue,x.Next1Id,x.Material1Id,x.Material1Value,x.AttributeValueIdType,x.AttributeValueId,x.EvolveFirstAncestor,x.ClothResetItemId}),process=c.CardProcesses.List.Single(x=>x.ProcessType==game::BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_CLOTHES_ENCHANT).ProcessDataList.Select(x=>new{x.Value,x.Probability,x.CostGold}),cloth=c.Items.Where(x=>x.Id>=280000011&&x.Id<=280000013).Select(x=>new{x.Id,x.NameAlias,x.ItemType}),gate=c.ContentValue(game::BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CLOTHES_ENCHANT)},new JsonSerializerOptions{WriteIndented=true}));return;
}
string folder=Path.GetFullPath(args.Single());
string allowed=Path.GetFullPath("I:/BnsmModernWork/Verification")+Path.DirectorySeparatorChar;
if(!folder.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated helper fixture required");
Directory.CreateDirectory(folder);
await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
var player=runtime.Store.GetOrCreate("local-player");
player.Level=99;player.Exp=ProgressionRules.PlayerFloor(runtime.Catalog,99);player.Nickname="助手试装";
player.HelperId=1;player.Inventory[220003004]=1;player.ClearStage=runtime.Catalog.Stages.Keys.Max();
player.Gold=1000000;player.Activities.Total["MCT_CLEAR_PVE"]=100;
foreach(uint type in new uint[]{1,2,3})player.HelperCostumes[type]=220003004;
player.AttendanceLastDay=player.ShortTermLastDay=(ProtocolRouter.Now+8*3600)/86400;
player.AttendanceDays=player.ShortTermDays=1;player.AttendanceLastTime=ProtocolRouter.Now;
player.AttendanceVipClaimedDay=player.AttendanceLastDay;runtime.Store.Save();
File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
await Task.Delay(Timeout.InfiniteTimeSpan);
