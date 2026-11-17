using System.Text.Json;
internal sealed class LocalFriends {
 [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
 public Dictionary<uint,LocalFriendLink> Links {get;set;}=[];
 public HashSet<uint> Incoming {get;set;}=[];
 public HashSet<uint> Outgoing {get;set;}=[];
 public ulong Day {get;set;}
 public HashSet<uint> SentToday {get;set;}=[];
 public HashSet<uint> ReceivedToday {get;set;}=[];
 public uint RefundCount {get;set;}
 public List<string> Inbox {get;set;}=[];
 public ulong LastChatTime {get;set;}
 public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
internal sealed class LocalFriendLink {
 [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
 public ulong Joined {get;set;}
 public ulong GiftDay {get;set;}
 public ulong SentAt {get;set;}
 public ulong ReceivedAt {get;set;}
}
