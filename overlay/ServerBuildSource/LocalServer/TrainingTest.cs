using System.Net.Sockets;
using System.Text.Json;

internal static class TrainingTest
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
    public static void Prepare(LocalRuntime runtime)
    {
        var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("local-player");p.Level=50;p.Exp=ProgressionRules.PlayerFloor(c,p.Level);p.Gold=1000000;p.CashPoint=10000;p.Stamina=10000;
        p.CardIds.Add(p.CardIds[0]);p.CardIds.Add(p.CardIds[0]);
        p.CardStates[6]=new(){Level=5,Exp=ProgressionRules.CardFloor(c,5)};p.CardStates[7]=new(){Level=5,Exp=ProgressionRules.CardFloor(c,5)};
        p.ClearStage=179;ulong now=ProtocolRouter.Now,day=(now+8*3600)/86400;
        p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.AttendanceLastTime=now;p.ShortTermDays=1;p.ShortTermLastDay=day;
        runtime.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated training fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            Prepare(runtime);var p=runtime.Store.GetOrCreate("local-player");var friend=runtime.Store.GetOrCreate("training-friend");
            friend.Nickname="修炼同伴";friend.Level=50;friend.Exp=ProgressionRules.PlayerFloor(runtime.Catalog,50);friend.CardIds.Add(friend.CardIds[0]);friend.CardStates[6]=new(){Level=5,Exp=50};
            friend.TrainingSlots[0]=new(){TrainingId=1,CardSequence=6,CardLevel=5,StartedAt=ProtocolRouter.Now,EndTime=ProtocolRouter.Now+3600,GoldCost=2500,FastCashCost=5,RewardExp=160};runtime.Store.Save();
        }
        // Deterministic helper item reward in this isolated UI fixture only.
        runtime.Catalog.Training[0].FriendRewardProb=100;
        await using var app=await DebugToolsTest.Host(runtime);
        app.MapPost("/fixture/training/expire",()=>{
            lock(runtime.Store.Gate){foreach(var t in runtime.Store.GetOrCreate("local-player").TrainingSlots.Values)t.EndTime=ProtocolRouter.Now-1;runtime.Store.Save();return Results.Json(new{ok=true});}
        });
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/training",Guid.NewGuid().ToString("N"));await using var server=new LocalRuntime(root,save,0,0,0,false);Prepare(server);
        using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.CardStates,p.CardIds,p.TrainingSlots,p.Inventory,p.TrainingHelpDay,p.TrainingHelpUsed,p.Deck,p.Decks});
        GsCardTrainingStartRequest Start(uint plan=1,ulong seq=6,uint slot=0)=>new(){keyInfo=peer.Key,trainId=plan,targetCardSeq=seq,slotIndex=slot};
        GSCardTrainingEndRequest End(CardTrainingEndType type,uint slot=0)=>new(){keyInfo=peer.Key,endType=type,slotIndex=slot};
        Task<GsCardTrainingListOk> List()=>peer.Request<GsCardTrainingListOk>(GameServerCommand.GS_CARD_TRAINING_LIST_REQUEST,new GsCardTrainingListRequest{keyInfo=peer.Key});
        async Task Bad(GameServerCommand cmd,object body,string label) {string before=State();await peer.Request<FailPacket>(cmd,body,false);Check(State()==before,label+" is rejected atomically");}
        var list=await List();Check(list.trainingList.Count==1&&list.trainingList[0].slotIndex==0&&list.trainingList[0].cardId==0&&list.remainHelpCount==5,"Original V0 has one free slot and five daily help attempts");
        p.Level=22;p.Exp=ProgressionRules.PlayerFloor(c,22);await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(),"Training below original level 23");p.Level=50;p.Exp=ProgressionRules.PlayerFloor(c,50);
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(slot:1),"VIP-locked training slot");
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(plan:99),"Unknown training plan");
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(seq:999),"Unowned training card");
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(seq:1),"Arena/castle defence card");
        uint oldLevel=p.CardStates[6].Level,oldExp=p.CardStates[6].Exp;
        int cardMax=ProgressionRules.CardMax(c,p,c.Cards.Single(x=>x.Id==p.CardIds[5]));
        p.CardStates[6].Level=(uint)cardMax;p.CardStates[6].Exp=ProgressionRules.CardCeiling(c,cardMax);
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(),"Card already at the current level cap");p.CardStates[6].Level=oldLevel;p.CardStates[6].Exp=oldExp;
        p.Gold=0;await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(),"Insufficient training gold");p.Gold=1000000;
        p.ActiveStage=13;await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(),"Training during battle");p.ActiveStage=null;
        p.Deck[0]=6;p.Decks[10]=[6];var req=Start();uint gold=p.Gold;var started=await peer.Request<GSCardTrainingStartOk>(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,req);
        Check(started.resultCard.training&&p.Gold==gold-2500&&p.TrainingSlots[0].EndTime>=ProtocolRouter.Now+3595&&p.Deck[0]==0,"Training starts with original cost, hour duration, busy flag and attack-deck removal");
        Check(p.Decks[10].SequenceEqual(new[]{6UL}),"Training preserves the original exhibition-only lineup");
        string state=State();await peer.Request<GSCardTrainingStartOk>(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,req,retry:true);Check(State()==state,"Start retry neither charges twice nor resets the timer");
        await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(seq:7),"Occupied training slot");
        await Bad(GameServerCommand.GS_LEVELUP_CARD_REQUEST,new GsCardLevelUpRequest{keyInfo=peer.Key,targetCardSeq=6,targetExp=51},"Paid upgrade of a training card");
        var sale=new GsSellUserCardRequest{keyInfo=peer.Key};sale.cardSeqList.Add(6);await Bad(GameServerCommand.GS_SELL_USER_CARD_REQUEST,sale,"Selling a training card");
        var deck=new GsUpdateUserDeckListRequest{keyInfo=peer.Key};deck.userDeckList.Add(new(){deckID=BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_SCENARIO,cardSlot1=6});await Bad(GameServerCommand.GS_UPDATE_USER_DECK_LIST_REQUEST,deck,"Putting a training card into a deck");
        await Bad(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,End(CardTrainingEndType.TRAINING_END_TIMEUP),"Early normal claim");
        await Bad(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,End(CardTrainingEndType.TRAINING_END_FAST),"Fast claim without buying acceleration");
        uint exp=p.CardStates[6].Exp;var cancel=End(CardTrainingEndType.TRAINING_END_GIVEUP);var cancelled=await peer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,cancel);
        Check(!cancelled.resultCard.training&&p.TrainingSlots.Count==0&&p.Gold==gold&&p.CardStates[6].Exp==exp,"Cancelling refunds all original gold, releases the card and grants no EXP");
        state=State();await peer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,cancel,retry:true);Check(State()==state,"Cancel retry cannot grant another refund");
        foreach(uint plan in new[]{1u,2u,3u}) {
            var rule=c.Training.Single(x=>x.Id==plan);var curve=c.TrainingExp.Single(x=>x.CardLevel==p.CardStates[6].Level);int index=curve.TrainingId.IndexOf((int)plan);
            exp=p.CardStates[6].Exp;await peer.Request<GSCardTrainingStartOk>(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(plan));
            var slot=p.TrainingSlots[0];Check(slot.EndTime-slot.StartedAt==(ulong)rule.TrainTime*3600&&slot.GoldCost==curve.ConsumeTrainGold[index]&&slot.RewardExp==curve.ExpValue[index],$"Plan {plan} snapshots original per-level price, duration and advertised EXP");
            p.CashPoint=0;await Bad(GameServerCommand.GS_CARD_TRAINING_FAST_REQUEST,new GsCardTrainingFastRequest{keyInfo=peer.Key,slotIndex=0},"Insufficient fast-training diamonds");p.CashPoint=10000;
            uint cash=p.CashPoint;var fast=new GsCardTrainingFastRequest{keyInfo=peer.Key,slotIndex=0};await peer.Request<GsCardTrainingFastOk>(GameServerCommand.GS_CARD_TRAINING_FAST_REQUEST,fast);
            Check(slot.Fast&&p.CashPoint==cash-slot.FastCashCost,$"Plan {plan} acceleration uses the original level-based diamond fee");
            state=State();await peer.Request<GsCardTrainingFastOk>(GameServerCommand.GS_CARD_TRAINING_FAST_REQUEST,fast,retry:true);Check(State()==state,$"Plan {plan} acceleration retry is idempotent");
            await Bad(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,End(CardTrainingEndType.TRAINING_END_GIVEUP),"Refund after acceleration");
            var claim=End(CardTrainingEndType.TRAINING_END_FAST);var result=await peer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,claim);
            uint ceiling=ProgressionRules.CardCeiling(c,ProgressionRules.CardMax(c,p,c.Cards.Single(x=>x.Id==p.CardIds[5])));
            Check(p.CardStates[6].Exp==Math.Min(ceiling,exp+slot.RewardExp)&&!result.resultCard.training&&p.TrainingSlots[0].CardSequence==0&&p.TrainingSlots[0].EndTime>ProtocolRouter.Now,$"Plan {plan} fast claim grants EXP once and preserves original-end-time slot cooldown");
            state=State();await peer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,claim,retry:true);Check(State()==state,$"Plan {plan} reward retry cannot duplicate EXP");
            await Bad(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(seq:7),"Start before fast-training cooldown ends");
            server.Store.Save();var saved=new PlayerStore(save,c).Find(p.Uid)!;Check(saved.TrainingSlots[0].CardSequence==0&&saved.TrainingSlots[0].EndTime==slot.EndTime&&saved.CardStates[6].Exp==p.CardStates[6].Exp,$"Plan {plan} reward and cooldown survive disk reload");
            p.TrainingSlots[0].EndTime=ProtocolRouter.Now-1;
        }
        await peer.Request<GSCardTrainingStartOk>(GameServerCommand.GS_CARD_TRAINING_START_REQUEST,Start(seq:7));exp=p.CardStates[7].Exp;p.TrainingSlots[0].EndTime=ProtocolRouter.Now-1;server.Store.Save();
        var restored=new PlayerStore(save,c).Find(p.Uid)!;Check(restored.TrainingSlots[0].CardSequence==7&&restored.CardStates[7].Exp==exp,"Offline-completed training remains claimable after loading without auto-crediting EXP");
        var poll=await peer.Request<GsCheckGameEntryOk>(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest{keyInfo=peer.Key});Check(poll.userCheckGameEntryData.trainingBadgeFlag,"Completed training sets the original town badge");
        await peer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,End(CardTrainingEndType.TRAINING_END_TIMEUP));Check(p.CardStates[7].Exp==exp+160&&p.TrainingSlots.Count==0,"Natural completion grants advertised EXP and frees the slot immediately");
        await Bad(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,End(CardTrainingEndType.TRAINING_END_TIMEUP),"Claiming the same completed slot with a new request");
        var friend=server.Store.GetOrCreate("training-friend");friend.Level=50;friend.Exp=ProgressionRules.PlayerFloor(c,50);friend.CardIds.Add(friend.CardIds[0]);friend.CardStates[6]=new(){Level=5,Exp=50};
        friend.TrainingSlots[0]=new(){TrainingId=1,CardSequence=6,CardLevel=5,EndTime=ProtocolRouter.Now+3600,StartedAt=ProtocolRouter.Now,RewardExp=160,GoldCost=2500,FastCashCost=5};c.Training[0].FriendRewardProb=100;
        list=await List();Check(list.trainingList.Any(x=>x.uid==friend.Uid&&x.cardSeq==6),"Other local-profile training is visible in the original assistance list");
        var help=new GsCardTrainingHelpFriendRequest{keyInfo=peer.Key,friendUid=friend.Uid,slotIndex=0};int held=p.Inventory.GetValueOrDefault(210002001u);
        var helped=await peer.Request<GsCardTrainingHelpFriendOk>(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help);
        Check(helped.remainCount==4&&helped.resultCardInfo.helpCount==1&&p.Inventory[210002001]==held+200,"Assistance consumes one daily attempt and persists original item rewards");
        state=State();await peer.Request<GsCardTrainingHelpFriendOk>(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help,retry:true);Check(State()==state&&friend.TrainingSlots[0].HelpCount==1,"Assistance retry cannot duplicate items or bonus counts");
        await Bad(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help,"Assistance within original ten-minute cooldown");
        friend.TrainingSlots[0].LastHelpAt=ProtocolRouter.Now-601;
        await peer.Request<GsCardTrainingHelpFriendOk>(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help);friend.TrainingSlots[0].LastHelpAt=ProtocolRouter.Now-601;
        await peer.Request<GsCardTrainingHelpFriendOk>(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help);
        await Bad(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help,"Fourth assistance of one training session");
        p.TrainingHelpDay=(ProtocolRouter.Now+8*3600)/86400;p.TrainingHelpUsed=5;friend.TrainingSlots[0].HelpCount=0;friend.TrainingSlots[0].LastHelpAt=0;
        await Bad(GameServerCommand.GS_CARD_TRAINING_HELP_FRIEND_REQUEST,help,"Exhausted daily help quota");p.TrainingHelpDay--;
        Check((await List()).remainHelpCount==5,"Daily assistance limit resets at the same UTC+8 day boundary");
        friend.TrainingSlots[0].HelpCount=3;friend.TrainingSlots[0].EndTime=ProtocolRouter.Now-1;
        using(var friendPeer=new Peer(root)) {
            await friendPeer.Login(server,"training-friend");
            var result=await friendPeer.Request<GsCardTrainingEndOk>(GameServerCommand.GS_CARD_TRAINING_END_REQUEST,new GSCardTrainingEndRequest{keyInfo=friendPeer.Key,slotIndex=0,endType=CardTrainingEndType.TRAINING_END_TIMEUP});
            Check(result.resultCard.cardExp==306&&friend.TrainingSlots.Count==0,"Assisted training applies the declared local 20-percent bonus per help once at settlement");
        }
        Check(c.Vip.All(x=>x.TrainingBonusSlotCount==0),"No extra VIP slots are invented when this recovered version's VIP table grants none");
        File.WriteAllText(Path.Combine(root,"evidence/training-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Training protocol: {checks.Count}/{checks.Count}");
    }
}
