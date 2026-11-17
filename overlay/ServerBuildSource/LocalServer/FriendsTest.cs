using System.Net.Sockets;
using System.Text.Json;
internal static class FriendsTest {
    private sealed class Peer(string root) : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();public byte[] Last=[];
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server,string account="local-player")
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),(await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload)!;
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false)
        {
            if(!retry)Key.sequenceNo++;body.GetType().GetField("keyInfo")?.SetValue(body,Key);
            var result=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)result.Command).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)result.Command+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),result.Payload)!).message);
            Last=result.Payload;if(typeof(T)==typeof(object))return(T)new object();
            var decoded=WireCodec.Deserialize(typeof(T),Last)!;
            if(decoded is GsGetFriendListOk friends)while(friends.currentCount<friends.totalCount){
                using var timeout=new CancellationTokenSource(10000);var next=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);
                if(next.Command!=result.Command)throw new Exception("Unexpected friend continuation");var part=(GsGetFriendListOk)WireCodec.Deserialize(typeof(GsGetFriendListOk),next.Payload)!;friends.friendList.AddRange(part.friendList);friends.currentCount+=part.currentCount;
            }
            return(T)decoded;
        }
        public Task<LocalEquipmentReply> Batch(LocalEquipmentRequest body)=>Request<LocalEquipmentReply>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,body);
        public void Dispose()=>socket.Dispose();
    }

    private static LocalPlayer Prepare(LocalRuntime r,string account,string nick){var p=r.Store.GetOrCreate(account);p.Nickname=nick;ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;p.Stamina=100;return p;}
    public static async Task Fixture(string root,string output){
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");Directory.CreateDirectory(output);
        await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(r.Store.All().Count==0){var p=Prepare(r,"local-player","好友测试");var q=Prepare(r,"friend-native","测试好友");q.Friends.Outgoing.Add(p.Uid);p.Friends.Incoming.Add(q.Uid);r.Store.Save();}
        await using var app=await DebugToolsTest.Host(r);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    public static async Task Run(string root){
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        string path=Path.Combine(@"I:\BnsmMobileWork\verification","friends-"+Guid.NewGuid().ToString("N"));
        await using(var r=new LocalRuntime(root,path,0,0,0,false)){
            var p=Prepare(r,"friends-a","甲方剑客");var q=Prepare(r,"friends-b","乙方剑客");var third=Prepare(r,"friends-c","丙方剑客");r.Store.Save();
            using var a=new Peer(root);using var b=new Peer(root);using var c=new Peer(root);await a.Login(r,p.Account);await b.Login(r,q.Account);await c.Login(r,third.Account);
            async Task Bad(Peer peer,GameServerCommand cmd,object body,string label){var result=await peer.Request<FailPacket>(cmd,body,false);Check(result.errorCode!=0,label);}
            async Task<GsGetFriendListOk> List(Peer peer,FriendStatus status=FriendStatus.FRIEND_STATUS_FRIEND_DONE)=>await peer.Request<GsGetFriendListOk>(GameServerCommand.GS_GET_FRIEND_LIST_REQUEST,new GsGetFriendListRequest{friendStatus=status});
            var empty=await List(a);Check(empty.totalCount==0&&empty.currentCount==0&&empty.remainHaveDrinkCount==10,"Empty native list terminates with original daily gift allowance");
            var rec=await a.Request<GsRecommendFriendOk>(GameServerCommand.GS_RECOMMEND_FRIEND_REQUEST,new GsRecommendFriendRequest());Check(rec.count>=8&&rec.recommendList.All(x=>x.uid!=p.Uid&&x.leaderCardInfo!=null),"Recommend local accounts and labelled offline companions");
            var bot=rec.recommendList.First(x=>x.uid>=900000);Check(bot.nick.EndsWith("·人机"),"Simulated companion is visibly labelled");
            var found=await a.Request<GsFindFriendOk>(GameServerCommand.GS_FIND_FRIEND_REQUEST,new GsFindFriendRequest{findNick="乙方"});Check(found.count==1&&found.findFriendList[0].uid==q.Uid,"Partial nickname search");
            found=await a.Request<GsFindFriendOk>(GameServerCommand.GS_FIND_FRIEND_REQUEST,new GsFindFriendRequest{findNick=q.Uid.ToString()});Check(found.count==1,"Exact UID search");
            await Bad(a,GameServerCommand.GS_FIND_FRIEND_REQUEST,new GsFindFriendRequest{findNick=""},"Reject empty search");
            await Bad(a,GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=p.Uid},"Reject self invitation");
            await Bad(a,GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=uint.MaxValue},"Reject unknown account");
            await a.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=q.Uid});Check(p.Friends.Outgoing.Contains(q.Uid)&&q.Friends.Incoming.Contains(p.Uid),"Invitation persisted on both accounts");
            await a.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=q.Uid},retry:true);Check(q.Friends.Incoming.Count==1,"Empty acknowledgement retry does not duplicate invitation");
            await Bad(a,GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=q.Uid},"Reject second invitation");
            var incoming=await List(b,FriendStatus.FRIEND_STATUS_BE_INVITED);Check(incoming.totalCount==1&&incoming.friendList[0].uid==p.Uid,"Native incoming requests list");
            var outgoing=await a.Request<GsGetInviteFriendListOk>(GameServerCommand.GS_GET_INVITE_FRIEND_LIST_REQUEST,new GsGetInviteFriendListRequest());Check(outgoing.totalCount==1&&outgoing.currentCount==1,"Outgoing requests count contract");
            await Bad(c,GameServerCommand.GS_ACCEPT_FRIEND_REQUEST,new GsAcceptFriendRequest{acceptUid=p.Uid},"Cannot accept a stranger's invitation");
            var accepted=await b.Request<GsAcceptFriendOk>(GameServerCommand.GS_ACCEPT_FRIEND_REQUEST,new GsAcceptFriendRequest{acceptUid=p.Uid});Check(accepted.friendData.uid==p.Uid&&accepted.rewardItem!=null&&p.Friends.Links.ContainsKey(q.Uid)&&q.Friends.Links.ContainsKey(p.Uid),"Accept creates reciprocal friendship and non-null native fields");
            await b.Request<GsAcceptFriendOk>(GameServerCommand.GS_ACCEPT_FRIEND_REQUEST,new GsAcceptFriendRequest{acceptUid=p.Uid},retry:true);Check(q.Friends.Links.Count==1,"Accept retry safe");
            await Bad(a,GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=q.Uid},"Reject existing friend");
            await Bad(c,GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=p.Uid},"Cannot gift a stranger");
            uint stamina=q.Stamina;var gift=await a.Request<GsHaveDrinkOk>(GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=q.Uid});Check(gift.remainDrinkCount==9&&gift.refundDrinkCount==1&&q.Friends.Links[p.Uid].GiftDay==p.Friends.Day,"Gift uses one original VIP quota and queues target stamina");
            await a.Request<GsHaveDrinkOk>(GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=q.Uid},retry:true);Check(p.Friends.RefundCount==1,"Gift retry does not duplicate refund");
            await Bad(a,GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=q.Uid},"Same contact may be gifted only once per day");
            var receive=await b.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=p.Uid});Check(receive.addStamina==6&&q.Stamina==stamina+6&&receive.remainReceiveDrinkCount==39,"Receive original six stamina");
            await b.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=p.Uid},retry:true);Check(q.Stamina==stamina+6,"Receive retry cannot duplicate stamina");
            await Bad(b,GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=p.Uid},"Already claimed gift rejected");
            stamina=p.Stamina;receive=await a.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest());Check(receive.addStamina==3&&p.Stamina==stamina+3&&p.Friends.RefundCount==0,"Native pig refund gives original three stamina");
            await Bad(a,GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest(),"Cannot reclaim pig refund");
            await a.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=bot.uid});Check(p.Friends.Links.ContainsKey(bot.uid),"Companion accepts without requiring another player");
            var list=await List(a);Check(list.friendList.Single(x=>x.uid==bot.uid).haveDrinkReceive==1,"Companion offers a daily stamina gift");
            p.Stamina=999;await Bad(a,GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=bot.uid},"Stamina cap preserves pending gift");p.Stamina=100;
            receive=await a.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=bot.uid});Check(p.Stamina==106,"Companion gift gives six stamina");
            await a.Request<object>(GameServerCommand.GS_DELETE_FRIEND_REQUEST,new GsDeleteFriendRequest{deleteUid=bot.uid});await a.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=bot.uid});await Bad(a,GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=bot.uid},"Delete and re-add cannot farm gifts");
            var name=await a.Request<GsGetAnotherUserGameInfoOk>(GameServerCommand.GS_GET_ANOTHER_USER_GAME_INFO_REQUEST,new GsGetAnotherUserGameInfoRequest{targetUid=q.Uid});Check(name.nameCardInfo.commonInfo.nick==q.Nickname&&name.nameCardInfo.leaderCardInfo.cardID==q.CardIds[0],"Original name card has consistent account and leader");
            name=await a.Request<GsGetAnotherUserGameInfoOk>(GameServerCommand.GS_GET_ANOTHER_USER_GAME_INFO_REQUEST,new GsGetAnotherUserGameInfoRequest{targetUid=bot.uid});Check(name.nameCardInfo.introMsg.Contains("人机"),"Companion profile declares offline simulation");
            await Bad(c,GameServerCommand.GS_GET_FRIEND_LIST_REQUEST,new GsGetFriendListRequest{targetUid=p.Uid},"Cannot read another account's private social graph");
            await Bad(c,GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=p.Uid,msg="你好"},"Private chat requires friendship");
            await a.Request<GsSendPrivateChatMessageOk>(GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=q.Uid,msg="你好"});Check(q.Friends.Inbox.Count==1,"Private message queued for offline friend");
            await a.Request<GsSendPrivateChatMessageOk>(GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=q.Uid,msg="你好"},retry:true);Check(q.Friends.Inbox.Count==1,"Chat send retry safe");
            await Bad(a,GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=q.Uid,msg="篡改"},"Private chat throttled");
            var inbox=await b.Request<GsFetchPrivateChatMessageOk>(GameServerCommand.GS_FETCH_PRIVATE_CHAT_MESSAGE_REQUEST,new GsFetchPrivateChatMessageRequest());Check(inbox.dataCount==1&&inbox.msgList[0].message=="你好"&&q.Friends.Inbox.Count==0,"Fetch receives original sender and consumes delivered inbox");
            inbox=await b.Request<GsFetchPrivateChatMessageOk>(GameServerCommand.GS_FETCH_PRIVATE_CHAT_MESSAGE_REQUEST,new GsFetchPrivateChatMessageRequest(),retry:true);Check(inbox.dataCount==1,"Fetch retry replays same delivery");
            inbox=await b.Request<GsFetchPrivateChatMessageOk>(GameServerCommand.GS_FETCH_PRIVATE_CHAT_MESSAGE_REQUEST,new GsFetchPrivateChatMessageRequest());Check(inbox.dataCount==0,"Already delivered message is not redelivered");
            p.Friends.LastChatTime=0;var answer=await a.Request<GsSendPrivateChatMessageOk>(GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=bot.uid,msg="你好"});Check(answer.dataCount==1&&answer.msgList[0].message.StartsWith("（本地伙伴）"),"Clearly labelled companion reply");
            await a.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=third.Uid});await c.Request<object>(GameServerCommand.GS_DENY_FRIEND_REQUEST,new GsDenyFriendRequest{denyUid=p.Uid});Check(!p.Friends.Outgoing.Contains(third.Uid)&&!third.Friends.Incoming.Contains(p.Uid),"Deny clears both sides");
            await a.Request<object>(GameServerCommand.GS_DELETE_FRIEND_REQUEST,new GsDeleteFriendRequest{deleteUid=q.Uid});Check(!p.Friends.Links.ContainsKey(q.Uid)&&!q.Friends.Links.ContainsKey(p.Uid),"Delete clears reciprocal friendship");
            await a.Request<object>(GameServerCommand.GS_DELETE_FRIEND_REQUEST,new GsDeleteFriendRequest{deleteUid=q.Uid},retry:true);Check(!q.Friends.Links.ContainsKey(p.Uid),"Delete retry safe");
            p.Friends.Day--;p.Friends.SentToday.Add(bot.uid);await List(a);Check(p.Friends.SentToday.Count==0&&p.Friends.ReceivedToday.Count==0,"UTC+8 midnight refreshes quotas");
            receive=await a.Request<GsReceiveHaveDrinkOk>(GameServerCommand.GS_RECEIVE_HAVE_DRINK_REQUEST,new GsReceiveHaveDrinkRequest{friendUid=bot.uid});Check(receive.addStamina==6,"Next day companion gift");
            for(int i=0;i<38;i++){var other=Prepare(r,"friend-bulk-"+i,"同伴"+i);p.Friends.Links[other.Uid]=new(){Joined=ProtocolRouter.Now};other.Friends.Links[p.Uid]=new(){Joined=ProtocolRouter.Now};}
            list=await List(a);Check(list.totalCount==39&&list.currentCount==39&&list.friendList.Count==39,"Encrypted native list spans three frames without hanging");
            var extra=Prepare(r,"friend-limit","限额测试");p.Friends.Links[extra.Uid]=new(){Joined=ProtocolRouter.Now};await Bad(a,GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=q.Uid},"Original forty-friend limit enforced");
            p.Friends.SentToday.Clear();p.Friends.RefundCount=0;
            var recipients=p.Friends.Links.Keys.Where(x=>x!=extra.Uid).Take(11).ToArray();
            for(int i=0;i<10;i++)await a.Request<GsHaveDrinkOk>(GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=recipients[i]});
            await Bad(a,GameServerCommand.GS_HAVE_DRINK_REQUEST,new GsHaveDrinkRequest{friendUid=recipients[10]},"Original VIP ten-gift daily limit enforced across contacts");
            Check(p.Friends.RefundCount==10,"Ten donations accumulate ten original refund units");
            uint mailTarget=p.Friends.Links.Keys.First(x=>r.Store.Find(x)!=null&&x!=extra.Uid);p.Friends.LastChatTime=0;
            await a.Request<GsSendPrivateChatMessageOk>(GameServerCommand.GS_SEND_PRIVATE_CHAT_MESSAGE_REQUEST,new GsSendPrivateChatMessageRequest{recieverUid=mailTarget,msg="离线保存测试"});
            var receiver=r.Store.Find(mailTarget)!;Check(receiver.Friends.Inbox.Count==1,"Unread mail saved for disconnected friend");
            var archive=JsonSerializer.Serialize(r.Store.All());Check(!archive.Contains("local-npc-900"),"Companions do not create phantom player accounts");
            r.Store.Save();
        }
        await using(var r=new LocalRuntime(root,path,0,0,0,false)){var p=r.Store.GetOrCreate("friends-a");Check(r.Store.All().Sum(x=>x.Friends.Inbox.Count)==1,"Unread private mail survives restart");Check(p.Friends.Links.Count==40&&p.Friends.ReceivedToday.Count==1,"Friends and daily claims survive full server restart");using var a=new Peer(root);await a.Login(r,p.Account);var list=await a.Request<GsGetFriendListOk>(GameServerCommand.GS_GET_FRIEND_LIST_REQUEST,new GsGetFriendListRequest{friendStatus=FriendStatus.FRIEND_STATUS_FRIEND_DONE});Check(list.totalCount==40&&list.friendList.Count==40,"Restarted encrypted list complete");}
        string home=Path.Combine(Path.GetDirectoryName(path)!,"friends-workspace-"+Guid.NewGuid().ToString("N"));var workspace=new SaveWorkspace(home);
        workspace.Command("list",legacy:path);string active=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(home,"workspace.json")))!["ActiveProfileId"]!.GetValue<string>();
        Check(File.ReadAllBytes(Path.Combine(workspace.Saves(active),"players.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(path,"players.json"))),"Legacy import preserves friend graph, gift ledger and unread messages byte for byte");
        string archivePath=Path.Combine(home,"friends.bnsm-save.zip");workspace.Command("export",id:active,file:archivePath);workspace.Command("import",file:archivePath,name:"好友存档导入");
        string imported=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(home,"workspace.json")))!["ActiveProfileId"]!.GetValue<string>();
        Check(File.ReadAllBytes(Path.Combine(workspace.Saves(imported),"players.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(path,"players.json"))),"Save archive round trip retains complete friend state");
        File.WriteAllText(Path.Combine(root,"evidence/friends-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Friends protocol: "+checks.Count+" checks passed");
    }
}
