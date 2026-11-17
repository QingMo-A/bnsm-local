using System.Text.Json;
internal static class ActivityTest
{
    private sealed class Peer : IDisposable
    {
        private readonly System.Net.Sockets.TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();public byte[] Last=[];
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(System.Net.Sockets.TcpClient client,ushort command,object request){await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);}
        public async Task Login(LocalRuntime server){using var login=new System.Net.Sockets.TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);}
        public async Task<T> Request<T>(GameServerCommand cmd,object req,bool success=true,bool retry=false){
            if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)cmd,req);string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(r.Item2).message);Last=r.Item2;
            var result=Read<T>(r.Item2);
            if(result is GsGetDebugPromotionInfoOk merged){using var timeout=new CancellationTokenSource(10000);while(merged.currentCount<merged.totalCount){var next=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);if(next.Command!=r.Item1)throw new InvalidDataException("Wrong activity continuation");var part=Read<GsGetDebugPromotionInfoOk>(next.Payload);merged.currentCount+=part.currentCount;merged.promotionDataList.AddRange(part.promotionDataList);}}
            return result;
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;


    private static LocalPlayer Prepare(LocalRuntime r)
    {
        var p=r.Store.GetOrCreate("local-player");p.Level=99;p.Exp=ProgressionRules.PlayerFloor(r.Catalog,99);p.Gold=500000;p.CashPoint=10000;p.Stamina=1000;
        ulong day=AttendanceRules.DayIndex(ProtocolRouter.Now);p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;r.Store.Save();return p;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated activity fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(r.Store.All().Count==0){var p=Prepare(r);ProtocolRouter.RefreshActivityDay(p.Activities,ProtocolRouter.Now);p.Activities.Daily["MCT_CLEAR_PVE"]=5;p.Activities.Weekly["MCT_CLEAR_PVE"]=5;r.Store.Save();}
        await using var app=await DebugToolsTest.Host(r);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromHours(2));
    }
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        string save=Path.Combine(root,"verification/activities",Guid.NewGuid().ToString("N"));
        await using(var server=new LocalRuntime(root,save,0,0,0,false)){
            var p=Prepare(server);using var peer=new Peer(root);await peer.Login(server);
            async Task<GsGetPromotionUserInfoOk> Info()=>await peer.Request<GsGetPromotionUserInfoOk>(GameServerCommand.GS_GET_PROMOTION_USER_INFO_REQUEST,new GsGetPromotionUserInfoRequest{keyInfo=peer.Key});
            GsReceivePromotionRewardRequest Claim(uint id,uint goal)=>new(){keyInfo=peer.Key,eventId=id,dataId=goal,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION};
            string State()=>JsonSerializer.Serialize(new{p.Activities,p.ActivityReceipts,p.Gold,p.CashPoint,p.Inventory});
            async Task Bad(GsReceivePromotionRewardRequest req,string label){string state=State();await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,req,false);Check(State()==state,label+" is atomic");}
            var config=new ActivityCatalog(root);Check(config.Events.Count==11&&config.Events.Sum(e=>e.Goals.Count)==36,"Eleven playable groups and thirty-six reward goals");
            var info=await Info();Check(info.debugEnable&&info.promotionInfo.Count==36,"Native event progress is populated");
            var defs=await peer.Request<GsGetDebugPromotionInfoOk>(GameServerCommand.GS_GET_DEBUG_PROMOTION_DATA_REQUEST,new GsGetDebugPromotionInfoRequest{keyInfo=peer.Key});
            Check(defs.totalCount==11&&defs.currentCount==11&&defs.promotionDataList.Count==11,"Native promotion definitions complete their multipart contract");
            Check(LocalRuntime.SerializeReplyParts(defs).All(b=>b.Length+WireCodec.HeaderSize<=WireCodec.MaxPacketSize),"Definition parts fit original client packets");
            Check(defs.promotionDataList.All(e=>e.PromotionStyle==BscTable.PROMOTION_STYLE.PROMOTION_STYLE_CONTENTS_BANNER_MISSION&&e.PromotionInfoList.All(g=>g.ItemId.Count==g.Quantity.Count&&g.Quantity.All(n=>n>0))),"Native goal text and displayed rewards agree with the server configuration");
            var notice=await peer.Request<GsGetSystemNoticeOk>(GameServerCommand.GS_GET_SYSTEM_NOTICE_REQUEST,new GsGetSystemNoticeRequest{keyInfo=peer.Key});Check(notice.noticeList.Count==3&&notice.noticeList.Sum(x=>x.Length)<240,"Announcements reduced to three concise entries");
            await Bad(Claim(1,1),"Unknown activity");await Bad(Claim(900001,999),"Unknown goal");var wrong=Claim(900001,1);wrong.promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CASH_CHARGE;await Bad(wrong,"Forged promotion type");
            await Bad(Claim(900002,1),"Incomplete progress");
            p.Inventory[210002001]=int.MaxValue;await Bad(Claim(900001,1),"Full reward inventory");p.Inventory.Remove(210002001);
            uint gold=p.Gold,cash=p.CashPoint;var claim=Claim(900001,1);var reward=await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,claim);
            Check(p.Gold==gold+30000&&p.CashPoint==cash+50&&p.Inventory[210002001]==500&&reward.earnedCoinInfo.gold==30000&&reward.earnedItemList.Single().itemCount==500,"Daily supply grants exact visible currencies and item delta");
            string before=State();var bytes=peer.Last.ToArray();await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,claim,retry:true);Check(before==State()&&bytes.SequenceEqual(peer.Last),"Retry replays exact receipt without a second grant");await Bad(Claim(900001,1),"Second fresh claim on the same day");
            info=await Info();Check(info.promotionRewardInfo.Single(x=>x.eventId==900001).status==1,"Native claimed stamp survives refresh");
            var entry=await peer.Request<GsCheckGameEntryOk>(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest{keyInfo=peer.Key});Check(entry.userCheckGameEntryData.hasNewEventRewardFlag,"HUD badge shows other unclaimed growth rewards");
            // A real board request contributes to daily and weekly activity goals, once per receipt.
            await peer.Request<GsGetBattleBoardPlayInfoOk>(GameServerCommand.GS_GET_BATTLE_BOARD_PLAY_INFO_REQUEST,new GsGetBattleBoardPlayInfoRequest{keyInfo=peer.Key});var cast=new GsCastDiceBattleBoardRequest{keyInfo=peer.Key};await peer.Request<GsCastDiceBattleBoardOk>(GameServerCommand.GS_CAST_DICE_BATTLE_BOARD_REQUEST,cast);
            Check(p.Activities.Daily["MCT_CAST_BATTLE_BOARD_DICE"]==1&&p.Activities.Weekly["MCT_CAST_BATTLE_BOARD_DICE"]==1,"Actual dice cast advances daily and weekly goals");before=State();await peer.Request<GsCastDiceBattleBoardOk>(GameServerCommand.GS_CAST_DICE_BATTLE_BOARD_REQUEST,cast,retry:true);Check(before==State(),"Replayed gameplay cannot duplicate activity progress");
            await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900003,1));
            var started=await peer.Request<GsNormalBattleStartOk>(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{keyInfo=peer.Key,stageid=13,deckid=1});
            var end=new GsNormalBattleEndConfirmRequest{keyInfo=peer.Key,stageId=13,timeStamp=started.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS};await peer.Request<GsNormalBattleEndConfirmOk>(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,end);
            Check(p.Activities.Daily["MCT_CLEAR_PVE"]==1&&p.Activities.Weekly["MCT_CLEAR_PVE"]==1,"Actual adventure settlement advances both activity periods");await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900002,1));
            for(uint id=1;id<=4;id++)await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900005,id));
            p.Activities.Weekly["LOGIN"]=3;await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900004,1));Check(p.Activities.WeeklyClaims.Contains("900004/1"),"Completed weekly goal grants and stores its own claim");await Bad(Claim(900004,1),"Repeated weekly reward");
            p.Activities.Day--;await Info();Check(p.Activities.DailyClaims.Count==0&&p.Activities.Daily.Count==1&&p.Activities.Weekly["MCT_CLEAR_PVE"]==1&&p.Activities.PermanentClaims.Count==4,"Next day refreshes daily goals while preserving weekly and permanent rewards");
            await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900001,1));await Bad(Claim(900005,1),"Permanent reward on a later day");
            p.Level=1;defs=await peer.Request<GsGetDebugPromotionInfoOk>(GameServerCommand.GS_GET_DEBUG_PROMOTION_DATA_REQUEST,new GsGetDebugPromotionInfoRequest{keyInfo=peer.Key});Check(defs.promotionDataList.All(e=>e.Id!=900003),"Low-level characters do not see the locked challenge activity");await Bad(Claim(900003,1),"Locked activity");p.Level=99;
            server.Store.Save();
        }
        await using(var server=new LocalRuntime(root,save,0,0,0,false)){var p=server.Store.All().Single();Check(p.Activities.PermanentClaims.Count==4&&p.Activities.DailyClaims.Contains("900001/1")&&p.Activities.Weekly["MCT_CLEAR_PVE"]==1,"Rewards, progress and receipts survive server restart");}
        var state=new LocalActivityState();ulong Sunday=(ulong)new DateTimeOffset(2026,11,1,23,59,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();ProtocolRouter.RefreshActivityDay(state,Sunday);state.DailyClaims.Add("d");state.WeeklyClaims.Add("w");state.PermanentClaims.Add("p");state.Weekly["MCT_CLEAR_PVE"]=30;ProtocolRouter.RefreshActivityDay(state,Sunday+120);
        Check(state.Day==AttendanceRules.DayIndex(Sunday+120)&&state.DailyClaims.Count==0&&state.WeeklyClaims.Count==0&&state.Weekly.GetValueOrDefault("MCT_CLEAR_PVE")==0&&state.PermanentClaims.Contains("p"),"Monday UTC+8 midnight resets daily and weekly state only");
        ProtocolRouter.RefreshActivityDay(state,Sunday+150);Check(state.Weekly["LOGIN"]==1,"Repeated requests on the same day cannot manufacture login days");
        File.WriteAllText(Path.Combine(root,"evidence/activities-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checks,save,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("ACTIVITIES_TEST_OK "+checks.Count);
    }
}
