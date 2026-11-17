extern alias game;
using System.Text.Json;
internal static class FriendsAudit {
 public static void Run(string root){var c=new GameCatalog(root); File.WriteAllText(Path.Combine(root,"evidence/friends-original-catalog.json"),JsonSerializer.Serialize(new {numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("FRIEND")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),players=c.PlayerStates.Select(x=>new{x.Level,x.MaxFriend}),vip=c.Vip.Select(x=>new{x.VipLv,x.MaxSendMeat})},new JsonSerializerOptions{WriteIndented=true}));}
}
