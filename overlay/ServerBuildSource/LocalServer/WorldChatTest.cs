using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

internal static class WorldChatTest
{
    private sealed class Peer(string root):IDisposable {
        readonly TcpClient socket=new();readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        internal SessionKeyInfo Key=new();
        async Task<(ushort Cmd,byte[] Body)> Send(TcpClient client,ushort cmd,object body){await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(body),true));using var timeout=new CancellationTokenSource(10000);var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);}
        internal async Task Login(LocalRuntime runtime){using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",runtime.LoginPort);await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="world-chat-wire",userPW="local",osType=DeviceOSType.DEVICE_PC});var r=await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true});var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),r.Body)!;Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",runtime.GamePort);}
        internal async Task<T> Request<T>(GameServerCommand cmd,object body,bool success=true,bool retry=false){if(!retry)Key.sequenceNo++;body.GetType().GetField("keyInfo")?.SetValue(body,Key);var r=await Send(socket,(ushort)cmd,body);if(((GameServerCommand)r.Cmd).ToString()!=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL"))throw new Exception("Unexpected chat protocol response: "+(GameServerCommand)r.Cmd);return(T)WireCodec.Deserialize(typeof(T),r.Body)!;}
        public void Dispose()=>socket.Dispose();
    }
    internal static async Task Fixture(string root,string output){
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated chat fixture required");Directory.CreateDirectory(output);
        await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(r.Store.All().Count==0){var p=r.Store.GetOrCreate("local-player");p.Nickname="聊天测试";p.Level=30;p.Exp=ProgressionRules.PlayerFloor(r.Catalog,30);ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;r.Store.Save();}
        await using var app=await DebugToolsTest.Host(r);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root){
        var examples=new List<object>();
        var checks=new List<string>();void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
        string area=Path.Combine(@"I:\BnsmMobileWork\verification","world-chat-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(area);var catalog=new GameCatalog(root);
        var settings=JsonSerializer.Deserialize<AmbientWorldChat.Settings>(File.ReadAllText(Path.Combine(root,"LocalServer/data/ambient-chat.json")))!;
        bool In(PKChatMessage m,string group)=>settings.Replies[group].Contains(m.message);
        foreach(var (text,previous,group) in new (string,string,string)[]{
            ("有人知道怎么升星吗","","star"),("你好，请问二技能怎么解锁","","skill.unlock"),("不是抽卡，是技能点不够","draw","skill.points"),
            ("不是问打不过，我问的是技能怎么升级","stage","skill"),("我说的不是升星，是超越","star","transcend"),("卡牌卡在99级了","","growth.cap"),
            ("第7关","stage","stage.detail"),("推图呢","social","social.stage"),("养卡","social","social.growth"),("前排","",""),("又是他","",""),("十连怎么又全是材料","","draw.badluck"),("抽卡的保底规则是什么","","draw.rules"),("终于抽到想要的了","","draw.good"),("我抽到了秦义绝","","draw.report"),("前排","stage","stage.defence"),("输出","stage","stage.damage"),("金币不够","skill","skill.gold"),("点数没了","skill","skill.points"),("又是他","draw","draw.duplicate"),("前排扛不住","stage","stage.defence"),("打到超时伤害不够","stage","stage.damage"),("还是不行","stage","stage.detail"),
            ("技能升级花金币太快了","","skill.gold"),("二技能点了报错","","skill.error"),("抽卡怎么全是重复的","","draw.duplicate"),
            ("绿色三星为什么是SS","","rarity"),("我想问升级和升星有什么区别","","growth"),("宝石怎么合成","","gem"),
            ("下级材料够了怎么合成","","materials"),("装备精炼在哪","","equipment"),("你好","","greeting"),("在吗","","greeting"),
            ("你是不是ai","","identity"),("今天的天气怎么样","",""),("小猫睡着了","",""),("谢谢","",""),("怎么合","star","star")})
            Check(WorldChatDialogue.Analyze(text,previous).Group==group,"Intent: "+text+" -> "+group);
        Check(WorldChatDialogue.Analyze("今天别聊抽卡了","draw").ClearContext,"Negated topic is not turned into draw advice");
        Check(WorldChatDialogue.Analyze("四星还没超越","skill","skill.unlock").Group=="skill.unlock","Skill unlock follow-up is not mistaken for a fresh transcendence question");
        Check(WorldChatDialogue.Analyze("别刷屏了").Quiet,"Quiet request recognized");
        ulong now=2000000000;string dir=Path.Combine(area,"logic");var chat=new AmbientWorldChat(root,dir,catalog,()=>now,new Random(17),chatter:false);
        var who=new PKUserNameCardInfo{uid=100001,commonInfo=new(){nick="小明",level=30}};var leader=new PKUserCardInfo{cardID=20101,cardLevel=1};uint key=0;
        PKChatMessage Send(string text){now+=4;return chat.Send("logic-"+(++key),text,who,leader);}
        List<PKChatMessage> Bots(uint after)=>chat.Fetch(after,0).Where(m=>m.senderUid>=900000).ToList();
        var initial=chat.Fetch(0,0);Check(initial.Count is >0 and <=2,"Small valid seed history instead of a fake busy backlog");uint cursor=initial.Max(m=>m.chatSeq);
        var hello=Send("你好，有人吗");Check(Bots(cursor).Count==0,"Bot never answers in the player's send frame");now+=5;Check(Bots(hello.chatSeq).Count==0,"Minimum human reply delay");now+=10;var greeting=Bots(hello.chatSeq);Check(greeting.Count==1&&In(greeting[0],"greeting"),"Delayed relevant greeting, one speaker only");
        examples.Add(new{input="你好，有人吗",reply=greeting[0].message});
        now+=35;var again=Send("你好，有人吗！");now+=15;Check(Bots(again.chatSeq).Count==0,"Punctuation-only repeated question is not answered again");
        now+=35;var stuck=Send("第7关打不过");now+=15;var question=Bots(stuck.chatSeq);Check(question.Count==1&&In(question[0],"stage.ask"),"Battle help asks about the actual failure");uint actor=question[0].senderUid;examples.Add(new{input="第7关打不过",reply=question[0].message,speaker=actor});
        now+=11;var defence=Send("前排扛不住");now+=15;var advice=Bots(defence.chatSeq);Check(advice.Count==1&&In(advice[0],"stage.defence")&&advice[0].senderUid==actor,"Follow-up keeps the same speaker and switches to defence advice");
        examples.Add(new{input="前排扛不住",reply=advice[0].message,speaker=advice[0].senderUid});
        now+=35;Send("这关又打不过了");var corrected=Send("不是问打不过，我问的是技能点不够");now+=15;var answer=Bots(corrected.chatSeq);Check(answer.Count==1&&In(answer[0],"skill.points"),"New correction cancels unsent old reply");
        examples.Add(new{input="不是问打不过，我问的是技能点不够",reply=answer[0].message});
        now+=35;Send("技能怎么升级");var unknown=Send("刚才那句算了，我去给猫加水");now+=15;Check(Bots(unknown.chatSeq).Count==0,"Unknown new speech cancels obsolete advice instead of inventing an answer");
        now+=35;var quiet=Send("别刷屏了，安静点");now+=180;Check(Bots(quiet.chatSeq).Count==0,"Quiet request suppresses ambient scripts and replies");
        chat=new AmbientWorldChat(root,dir,catalog,()=>now,new Random(18),chatter:false);Check(Bots(quiet.chatSeq).Count==0,"Mute, context and pending timing survive server restart");now+=125;int resumed=0;for(int i=0;i<10&&resumed==0;i++){resumed=Bots(quiet.chatSeq).Count;if(resumed==0)now+=60;}Check(resumed==1,"Quiet expires naturally with one message, not a burst");
        now+=35;var off=Send("技能怎么升级");now+=86400;var returned=Bots(off.chatSeq);Check(returned.Count==1&&!In(returned[0],"skill"),"An offline gap discards old questions and unfinished scripts");
        // Context is per account; two simultaneous speakers receive named, paced answers.
        now+=35;var one=Send("武功点多久恢复");who=new(){uid=100002,commonInfo=new(){nick="小红",level=20}};var two=Send("升星怎么看碎片");now+=15;var first=Bots(two.chatSeq);Check(first.Count==1&&(first[0].message.StartsWith("小明，")||first[0].message.StartsWith("小红，")),"Concurrent question is addressed to its own sender");
        Check(Bots(first[0].chatSeq).Count==0,"More polls cannot make the second reply immediate");now+=10;var second=Bots(first[0].chatSeq);Check(second.Count==1&&second[0].message[..3]!=first[0].message[..3],"Another player's context is paced and kept separate");
        // Legacy chat JSON migrates without changing player speech or sequence numbers.
        var legacy=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(dir,"world-chat.json")));var legacyDir=Path.Combine(area,"legacy");Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir,"world-chat.json"),JsonSerializer.Serialize(new{LastSequence=legacy.GetProperty("LastSequence").GetUInt32(),Messages=legacy.GetProperty("Messages")}));
        var migrated=new AmbientWorldChat(root,legacyDir,catalog,()=>now,new Random(1),chatter:false);Check(migrated.Fetch(0,0).Any(m=>m.chatSeq==two.chatSeq&&m.message==two.message),"Old world-chat history and exact player text retained");
        var restart=new AmbientWorldChat(root,dir,catalog,()=>now,new Random(2),chatter:false);Check(restart.Fetch(0,0).Count==10,"Original ten-message frame limit remains intact");
        // 两小时空闲模拟：频道要一直有人说话，但不能刷屏、不能复读、不能一个人连着说。
        string simDir=Path.Combine(area,"simulation");ulong simNow=3000000000;var sim=new AmbientWorldChat(root,simDir,catalog,()=>simNow,new Random(73));var rows=new List<PKChatMessage>();bool boundedPolls=true;uint last=sim.Fetch(0,0).Max(m=>m.chatSeq);
        for(int i=0;i<120;i++){simNow+=60;var batch=sim.Fetch(last,0);boundedPolls&=batch.Count<=1;rows.AddRange(batch);if(batch.Count>0)last=batch.Max(m=>m.chatSeq);}
        Check(boundedPolls,"120 consecutive idle polls each emit at most one line");
        Check(rows.Count is >=60 and <=150,"Two hours of idle chatter stays lively without flooding");
        Check(rows.Select(m=>WorldChatDialogue.Normalize(m.message)).Distinct().Count()*5>=rows.Count*4,"Almost every ambient line inside two hours is a different one");
        var lastSeen=new Dictionary<string,ulong>();bool repeated=false;
        foreach(var m in rows){string line=WorldChatDialogue.Normalize(m.message);if(lastSeen.TryGetValue(line,out ulong when)&&m.timeStamp<when+2400)repeated=true;lastSeen[line]=m.timeStamp;}
        Check(!repeated,"No ambient line repeats inside forty minutes");
        Check(rows.Select((m,i)=>i>0&&m.senderUid==rows[i-1].senderUid).Count(x=>x)<=4,"The channel is not one person talking to themselves");
        Check(rows.Count(m=>m.message.Contains("门派")||m.message.Contains("势力战")||m.message.Contains("领地"))>=4,"Recruiting and party calls appear between the chatter");
        // 底噪要像玩家在说话：池子里得有成规模的口语和自造词，而不是清一色说明书。
        Check(settings.Chatter.Count(l=>l.Text.Contains("寄")||l.Text.Contains("麻")||l.Text.Contains("歪")||l.Text.Contains("摆烂")||l.Text.Contains("血亏")||l.Text.Contains("白嫖")||l.Text.Contains("出货"))>=6,"Slang and loose wording are part of the channel voice");
        Check(settings.Chatter.Count(l=>l.Text.Length<=12)*2>=settings.Chatter.Count,"Chatter is short like real chat, not paragraphs");
        // Retention is bounded even when many people send unrecognized messages.
        for(int i=0;i<215;i++){now+=4;chat.Send("bulk-"+i,"无关闲话"+i,new(){uid=(uint)(110000+i),commonInfo=new(){nick="测试",level=1}},leader);}
        var stored=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(dir,"world-chat.json")));Check(stored.GetProperty("Messages").GetArrayLength()==200&&stored.GetProperty("Contexts").EnumerateObject().Count()<=32&&stored.GetProperty("Pending").GetArrayLength()<=3,"History, per-user contexts and reply backlog stay bounded");
        await using(var runtime=new LocalRuntime(root,Path.Combine(area,"wire"),0,0,0,false)){
            using var peer=new Peer(root);await peer.Login(runtime);
            var seed=await peer.Request<GsFetchPublicChatMessageOk>(GameServerCommand.GS_FETCH_PUBLIC_CHAT_MESSAGE_REQUEST,new GsFetchPublicChatMessageRequest());uint seen=seed.msgList.Max(m=>m.chatSeq);
            var request=new GsSendPublicChatMessageRequest{msg="你好，有人吗",lastMsgSeq=seen};var posted=await peer.Request<GsSendPublicChatMessageOk>(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,request);Check(posted.msgList.Count==1&&posted.msgList[0].senderUid==peer.Key.uid,"Encrypted original send response contains the actual player, not an instant NPC");
            var retry=await peer.Request<GsSendPublicChatMessageOk>(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,request,retry:true);Check(retry.msgList[0].chatSeq==posted.msgList[0].chatSeq,"Chat retry is idempotent");request.msg="same sequence different question";await peer.Request<FailPacket>(GameServerCommand.GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST,request,false,true);Check(true,"Changed replay rejected");
            await Task.Delay(14000);var reply=await peer.Request<GsFetchPublicChatMessageOk>(GameServerCommand.GS_FETCH_PUBLIC_CHAT_MESSAGE_REQUEST,new GsFetchPublicChatMessageRequest{lastMsgSeq=posted.msgList[0].chatSeq});Check(reply.msgList.Count==1&&In(reply.msgList[0],"greeting")&&reply.msgList[0].leaderCardInfo.cardID>0,"Delayed natural reply crosses the real encrypted protocol with a valid portrait");
        }
        File.WriteAllText(Path.Combine(root,"evidence/world-chat-dialogue-examples.json"),JsonSerializer.Serialize(examples,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        File.WriteAllText(Path.Combine(root,"evidence/world-chat-test.json"),JsonSerializer.Serialize(new{passed=true,checks,productionSaveUsed=false,offlineOnly=true},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("World chat: "+checks.Count+" checks passed");
    }
}
