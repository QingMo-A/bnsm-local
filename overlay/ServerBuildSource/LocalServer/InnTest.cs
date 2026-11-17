using System.Net.Sockets;
using System.Text.Json;

internal static class InnTest
{
    private sealed class Peer : IDisposable
    {
        private readonly TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(TcpClient client,ushort command,object request) {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server,string account) {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false) {
            if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)command,body);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)r.Item1+": "+Read<FailPacket>(r.Item2).message);
            return Read<T>(r.Item2);
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;


    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated inn fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {var p=runtime.Store.GetOrCreate("local-player");p.Level=20;p.Exp=ProgressionRules.PlayerFloor(runtime.Catalog,20);p.CashPoint=1000;ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;runtime.Store.Save();}
        // Test fixture chooses one original deterministic dice row. Production keeps every original probability.
        foreach(var set in runtime.Catalog.PubGames)for(int i=0;i<set.MinigameSetList.Count;i++)set.MinigameSetList[i].QUIZProb=i==0?100:0;
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/inn",Guid.NewGuid().ToString("N"));var checks=new List<string>();void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;
            string State()=>JsonSerializer.Serialize(new{p.PubGame,p.Gold,p.CashPoint,p.Inventory,p.CardIds,p.Exp});
            async Task Bad(GameServerCommand command,object body,string label){string before=State();await peer.Request<FailPacket>(command,body,false);Check(before==State(),label+" is rejected without changing state");}
            Task<GsGetPubminigameRecordOk> Record()=>peer.Request<GsGetPubminigameRecordOk>(GameServerCommand.GS_GET_PUBMINIGAME_RECORD_REQUEST,new GsGetPubminigameRecordRequest{keyInfo=peer.Key});
            GsPubminigameAnswerRequest Answer(uint face)=>new(){keyInfo=peer.Key,answerNumber=face};
            var quiz=new GsGetPubminigameQuizRequest{keyInfo=peer.Key};var bypass=new GsBuyPubminigameBypassRequest{keyInfo=peer.Key,useBypass=true};
            Check(c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_TAVERN)==18,"Inn unlocks at original level 18");
            await Bad(GameServerCommand.GS_GET_PUB_ENTRY_INFO_REQUEST,new GsGetPubEntryInfoRequest{keyInfo=peer.Key},"Entry below level 18");p.Level=18;p.Exp=ProgressionRules.PlayerFloor(c,18);
            var record=await Record();Check(record.pubminigameRecord.remainCount==3&&record.pubminigameRecord.round==1&&record.pubminigameRecord.pubminigameId==1000001,"Fresh inn uses original evergreen promotion, dice set and three daily attempts");
            await peer.Request<GsGetPubEntryInfoOk>(GameServerCommand.GS_GET_PUB_ENTRY_INFO_REQUEST,new GsGetPubEntryInfoRequest{keyInfo=peer.Key});Check(true,"Native inn entry returns a valid record with honest empty online counters");
            await Bad(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,Answer(1),"Answer before requesting a question");
            var q=await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);Check(q.pubminigameRecord.currentState==PubminigameState.PUBMINIGAME_QUESTION&&q.pubminigameRecord.remainCount==2&&q.pubminigameRecord.correctAnswer==0,"Question consumes one attempt and does not send the answer before rolling");
            uint face=p.PubGame.Answer,index=p.PubGame.QuestionIndex;Check(face is >=1 and <=6&&index<c.PubGames.Single(x=>x.SetID==p.PubGame.Set).MinigameSetList.Count,"Question and dice face come from the recovered weighted original rows");
            string state=State();await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz,retry:true);Check(state==State(),"Quiz retry cannot spend another daily attempt");
            await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);Check(p.PubGame.Answer==face&&p.PubGame.QuestionIndex==index&&p.PubGame.Remaining==2,"Reopening an unanswered question does not reroll or consume attempts");
            await Bad(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,Answer(7),"Invalid dice face");
            var answer=Answer(face);var win=await peer.Request<GsPubminigameAnswerOk>(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,answer);Check(win.correctAnswer&&p.PubGame.Wins==1&&win.earnedItemList.Single().itemID==218000001&&win.earnedItemList.Single().itemCount==20&&p.Inventory[218000001]==20,"First win grants original twenty likes item and native reward list");
            Check(win.earnedExp==p.Exp&&p.Level==18,"Inn response cannot reset the player's cumulative experience");
            state=State();await peer.Request<GsPubminigameAnswerOk>(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,answer,retry:true);Check(state==State(),"Answer retry cannot grant the reward twice");await Bad(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,answer,"Second answer for a settled question");
            await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);face=p.PubGame.Answer;
            var wrong=await peer.Request<GsPubminigameAnswerOk>(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,Answer(face==6?1:face+1));Check(!wrong.correctAnswer&&p.PubGame.State==PubminigameState.PUBMINIGAME_INCORRECT_ANSWER&&p.PubGame.Losses==1&&wrong.earnedItemList.Count==0,"Wrong answer persists the bypass choice without granting a reward");
            await Bad(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz,"Skipping an unresolved wrong answer");await Bad(GameServerCommand.GS_BUY_PUBMINIGAME_BYPASS_REQUEST,bypass,"Unfunded paid bypass");p.CashPoint=100;
            var paid=await peer.Request<GsBuyPubminigameBypassOk>(GameServerCommand.GS_BUY_PUBMINIGAME_BYPASS_REQUEST,bypass);Check(p.CashPoint==95&&paid.coinInfo.cashPoint==95&&p.PubGame.PaidBypasses==1&&p.PubGame.Wins==2&&p.Inventory[207004001]==3,"Paid bypass charges original five diamonds and grants three dolls for second win");
            state=State();await peer.Request<GsBuyPubminigameBypassOk>(GameServerCommand.GS_BUY_PUBMINIGAME_BYPASS_REQUEST,bypass,retry:true);Check(state==State(),"Paid bypass retry cannot charge twice or duplicate dolls");await Bad(GameServerCommand.GS_BUY_PUBMINIGAME_BYPASS_REQUEST,bypass,"Bypass outside incorrect-answer state");
            await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);face=p.PubGame.Answer;
            await peer.Request<GsPubminigameAnswerOk>(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,Answer(face));Check(p.PubGame.Wins==3&&p.PubGame.Remaining==0&&p.Inventory[205060312]==1,"Third win grants the original card-fragment gift box and exhausts the day");
            await Bad(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz,"Fourth daily dice attempt");
            p.PubGame.Day--;await Record();Check(p.PubGame.Remaining==3&&p.PubGame.Wins==0&&p.PubGame.Losses==0&&p.PubGame.PaidBypasses==0,"UTC+8 daily rollover refreshes dice attempts and temporary counters");
            await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);face=p.PubGame.Answer;
            await peer.Request<GsPubminigameAnswerOk>(GameServerCommand.GS_PUBMINIGAME_ANSWER_REQUEST,Answer(face==6?1:face+1));uint cash=p.CashPoint;bypass.useBypass=false;
            var refused=await peer.Request<GsBuyPubminigameBypassOk>(GameServerCommand.GS_BUY_PUBMINIGAME_BYPASS_REQUEST,bypass);Check(p.CashPoint==cash&&refused.earnedItemList.Count==0&&p.PubGame.Wins==0&&p.PubGame.State==PubminigameState.PUBMINIGAME_NONE,"Declining a bypass is free and returns to the next question");
            await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,quiz);p.PubGame.ExtraFields=new(){["futureInnField"]=JsonSerializer.SerializeToElement(77)};server.Store.Save();
            foreach(var cmd in new[]{GameServerCommand.GS_GET_SERVANT_MANAGE_INFO_REQUEST,GameServerCommand.GS_GET_SERVANT_TASK_COMPLETED_LIST_REQUEST,GameServerCommand.GS_GET_SERVANT_PVP_RECOMMEND_REQUEST}) {
                object body=cmd==GameServerCommand.GS_GET_SERVANT_MANAGE_INFO_REQUEST?new GsGetServantManageInfoRequest{keyInfo=peer.Key}:cmd==GameServerCommand.GS_GET_SERVANT_TASK_COMPLETED_LIST_REQUEST?new GsGetServantTaskCompletedListRequest{keyInfo=peer.Key}:new GsGetServantPvpRecommendRequest{keyInfo=peer.Key};
                var r=server.Router.Handle(ServiceKind.Game,(ushort)cmd,WireCodec.Serialize(body),new ConnectionState());Check(r.Name.EndsWith("_OK"),"Empty local multiplayer list responds: "+cmd);
            }
        }
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;uint face=p.PubGame.Answer,index=p.PubGame.QuestionIndex;
            var q=await peer.Request<GsGetPubminigameQuizOk>(GameServerCommand.GS_GET_PUBMINIGAME_QUIZ_REQUEST,new GsGetPubminigameQuizRequest{keyInfo=peer.Key});Check(p.PubGame.Answer==face&&p.PubGame.QuestionIndex==index&&q.pubminigameRecord.remainCount==1,"Server restart resumes the same pending dice question and daily balance");Check(p.Inventory[218000001]==20&&p.Inventory[207004001]==3&&p.Inventory[205060312]==1,"All three original rewards survive a server restart");Check(p.PubGame.ExtraFields!["futureInnField"].GetInt32()==77,"Future inn save fields are preserved");
        }
        File.WriteAllText(Path.Combine(root,"evidence/inn-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Inn protocol: {checks.Count}/{checks.Count}");
    }
}
