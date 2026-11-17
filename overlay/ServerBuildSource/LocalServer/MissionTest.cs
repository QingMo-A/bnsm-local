using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;

internal static class MissionTest
{
    private sealed class Peer(string root):IDisposable
    {
        private readonly TcpClient socket=new(){NoDelay=true};
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();public byte[] Last=[];
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort command,object body)
        {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(body),true));using var timeout=new CancellationTokenSource(12000);
            var frame=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(frame.Command,frame.Payload);
        }
        public async Task Login(LocalRuntime server)
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="mission-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),(await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload)!;
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false)
        {
            if(!retry)Key.sequenceNo++;body.GetType().GetField("keyInfo")!.SetValue(body,Key);
            var r=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Command).ToString()!=expected)throw new Exception(expected+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),r.Payload)!).message);
            Last=r.Payload;return(T)WireCodec.Deserialize(typeof(T),Last)!;
        }
        public Task<GsGetProgressingMissionListOk> List()=>Request<GsGetProgressingMissionListOk>(GameServerCommand.GS_GET_PROGRESSING_MISSION_LIST_REQUEST,new GsGetProgressingMissionListRequest());
        public Task<GsGetChangedMissionListOk> Changed()=>Request<GsGetChangedMissionListOk>(GameServerCommand.GS_GET_CHANGED_MISSION_LIST_REQUEST,new GsGetChangedMissionListRequest());
        public Task<GsMissionRewardOk> Claim(uint id,bool retry=false)=>Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=id},retry:retry);
        public Task<FailPacket> Bad(uint id)=>Request<FailPacket>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=id},false);
        public void Dispose()=>socket.Dispose();
    }
    private static LocalPlayer Prepare(LocalRuntime r,string account)
    {
        var p=r.Store.GetOrCreate(account);p.Level=99;p.Exp=(uint)r.Catalog.PlayerStates.Single(x=>x.Level==99).Exp;p.Gold=20_000_000;p.CashPoint=900000;p.Stamina=10000;
        p.CardIds=r.Catalog.InitialHeroes.Take(12).Select(x=>(uint)x.Id).ToList();p.CollectedCardIds.UnionWith(p.CardIds);p.Deck=[1,2,3,4,5];
        foreach(ulong seq in p.Deck)p.CardStates[seq]=new(){Level=99};
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
        r.Store.Save();return p;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        typeof(ProtocolRouter).GetField("notices",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(r.Router,Array.Empty<string>()); // Skip unrelated launch announcements in this isolated visual fixture.
        if(r.Store.All().Count==0){var p=Prepare(r,"local-player");p.ClearStage=30;p.StageRecords[14]=new(){Stars=3,PerfectTime=30};p.FoundedGuilds.Add(new(){Id=1,Name="浠诲姟楠岃瘉闂ㄦ淳",Level=1,Created=ProtocolRouter.Now,Master=p.Uid});p.FoundedGuilds[0].Members[p.Uid]=new(){Joined=ProtocolRouter.Now};p.Guild.GuildId=1;r.Store.Save();}
        await using var app=await DebugToolsTest.Host(r);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromHours(2));
    }
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool b,string text){if(!b)throw new Exception(text);checks.Add(text);}
        string save=Path.Combine(root,"verification/missions",Guid.NewGuid().ToString("N"));
        var protectedFiles=Directory.Exists(SaveWorkspace.DefaultRoot)?Directory.GetFiles(SaveWorkspace.DefaultRoot,"players.json",System.IO.SearchOption.AllDirectories):[];
        var hashes=protectedFiles.ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        var mc=new MissionCatalog(root);var permanent=new Dictionary<uint,ulong>();
        string State(LocalPlayer p)=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Exp,p.Level,p.Stamina,p.CardIds,p.Inventory,p.Missions,p.MissionReceipts});
        await using(var server=new LocalRuntime(root,save,0,0,0)) {
            var p=Prepare(server,"mission-test");using var peer=new Peer(root);await peer.Login(server);
            var list=await peer.List();
            Check(mc.Rows.Count==575,"all 575 shipped missions loaded without changing tables");
            Check(list.list.Any(x=>mc.Rows[x.missionId].MissionType==BscTable.MISSION_TYPE.ACHIEVE_MISSION),"achievement chains populated");
            Check(list.list.Any(x=>mc.Rows[x.missionId].MissionType==BscTable.MISSION_TYPE.DAILY_MISSION),"level-appropriate daily tasks populated");
            Check(list.list.Count(x=>mc.Rows[x.missionId].MissionType==BscTable.MISSION_TYPE.COLLECT_MISSION)==36,"all 36 native deck challenges populated");
            Check(list.list.All(x=>mc.Rows[x.missionId].IconFlag!="HUDICONS_GUILD"),"personal guild tasks require actual membership");
            Check((await peer.Changed()).list.Count==0,"unchanged polls do not resend completed-task notifications");
            var mealTask=list.list.FirstOrDefault(x=>mc.Rows[x.missionId].CheckType==BscTable.CHECK_TYPE.MCT_VISIT_MY_HOME&&x.status==MissionStatus.MISSION_STATUS_COMPLETED);
            if(mealTask!=null){uint stamina=p.Stamina;var mealReply=await peer.Claim(mealTask.missionId);Check(p.Stamina==stamina+mealReply.rewardStamina,"timed task stamina stacks above natural recovery cap");}
            string before=State(p);await peer.Bad(uint.MaxValue);Check(before==State(p),"unknown mission cannot grant rewards");
            await peer.Bad(1001);Check(before==State(p),"locked successor cannot be claimed");
            await peer.Bad(10001);Check(before==State(p),"owning a deck alone does not complete its stage-entry mission");
            Check(list.list.Single(x=>x.missionId==3000).status==MissionStatus.MISSION_STATUS_COMPLETED,"existing collection bootstraps original achievement progress");
            var row=mc.Rows[3000];uint item=(uint)row.RewardId.Single();int oldItem=p.Inventory.GetValueOrDefault(item);
            p.Inventory[item]=int.MaxValue;before=State(p);await peer.Bad(3000);Check(before==State(p),"overflow rejects the entire reward without marking claimed");p.Inventory[item]=oldItem;
            var reward=await peer.Claim(3000);byte[] first=peer.Last.ToArray();before=State(p);
            var replay=await peer.Claim(3000,true);Check(first.SequenceEqual(peer.Last)&&before==State(p),"same encrypted request replays exact reward receipt once");
            Check(p.Inventory[item]==oldItem+row.RewardQuantity.Single()&&reward.rewardItemInfoList.Single().itemCount==row.RewardQuantity.Single(),"original item reward and native delta agree");
            await peer.Bad(3000);Check(before==State(p),"a new sequence cannot claim the same achievement twice");
            var changed=await peer.Changed();Check(changed.deletedDailyMissionIdList.Contains(3000)&&changed.list.Any(x=>x.missionId==3001),"claim removes old row and unlocks its successor immediately");
            p.ClearHardStage=13;p.HardStageRecords[13]=new(){Stars=3,PerfectTime=35};server.Store.Save();
            await peer.Changed();uint cash=p.CashPoint;await peer.Claim(1100);Check(p.CashPoint==cash+10,"hard stage achievement uses original diamond reward");
            changed=await peer.Changed();Check(changed.list.Single(x=>x.missionId==1300).status==MissionStatus.MISSION_STATUS_COMPLETED,"perfect-clear achievement reads saved three-star time");
            p.HardStageRecords[13].PerfectTime=41;before=State(p);await peer.Bad(1300);Check(before==State(p),"perfect-clear time limit is enforced by the server");p.HardStageRecords[13].PerfectTime=35;
            await peer.Claim(1300);Check(p.CashPoint==cash+40,"perfect-clear reward remains exact and separate from base clear");
            await peer.Request<GsGetChangedMissionListOk>(GameServerCommand.GS_GET_CHANGED_MISSION_LIST_REQUEST,new GsGetChangedMissionListRequest{needBattleAbilityRefresh=true,whiteTigerBattleAbility=98765});
            Check(p.Ranking.TigerPower==98765&&p.Ranking.HasPower,"mission refresh still updates ranking battle power");
            var daily=mc.Rows.Values.Single(x=>x.CheckType==BscTable.CHECK_TYPE.MCT_GACHA&&x.OpenConditionValue[0]==81);uint id=(uint)daily.MissionId;
            var draw=new GsGachaRequest{gachaGroupIndex=1,useFreeCount=true};
            await peer.Request<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST,draw);before=State(p);
            await peer.Request<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST,draw,retry:true);Check(before==State(p),"gacha receipt retry does not add mission progress twice");
            Check(p.Missions.DailyCounters[BscTable.CHECK_TYPE.MCT_GACHA]==1,"one free draw counts as one draw");
            for(int i=1;i<daily.NeedCount;i++)await peer.Request<GsGachaOk>(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest{gachaGroupIndex=1});
            changed=await peer.Changed();Check(changed.list.Single(x=>x.missionId==id).status==MissionStatus.MISSION_STATUS_COMPLETED,"real encrypted draws complete the daily task");
            uint gold=p.Gold,exp=p.Exp;reward=await peer.Claim(id);
            Check(p.Gold==gold+(uint)daily.RewardQuantity[daily.RewardIdType.IndexOf(BscTable.REWARD_TYPE.REWARD_TYPE_GOLD)]&&p.Exp==exp+(uint)daily.RewardQuantity[daily.RewardIdType.IndexOf(BscTable.REWARD_TYPE.REWARD_TYPE_PC_EXP)]&&reward.level==p.Level&&reward.stamina==p.Stamina,"daily reward includes original gold, experience and native player state");
            before=State(p);await peer.Bad(id);Check(before==State(p),"daily reward cannot be claimed twice today");
            p.Missions.Day--;server.Store.Save();list=await peer.List();
            Check(!p.Missions.DailyClaimed.ContainsKey(id)&&p.Missions.DailyCounters.Count==0&&list.list.Single(x=>x.missionId==id).count==0,"next mission day refreshes daily progress and claim state");
            Check(p.Missions.Claimed.ContainsKey(3000)&&p.Missions.Claimed.ContainsKey(1100),"daily refresh preserves permanent achievements");
            p.ClearStage=26;var deckMission=mc.Rows[10001];var required=mc.Decks[deckMission.Check2Id];
            p.CardIds=required.AICardInfoList.Select(x=>(uint)x.CardId).ToList();p.Deck=Enumerable.Range(1,p.CardIds.Count).Select(x=>(ulong)x).Concat(Enumerable.Repeat(0UL,5-p.CardIds.Count)).ToList();
            p.CardStates.Clear();foreach(ulong seq in p.Deck.Where(x=>x>0))p.CardStates[seq]=new(){Level=99};server.Store.Save();
            var started=await peer.Request<GsNormalBattleStartOk>(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{stageid=26,deckid=1,difficulty=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL});
            Check(p.Missions.EnteredDeckMissions.Contains(10001),"actual matching deck entering original target stage completes collection challenge");
            changed=await peer.Changed();Check(changed.list.Single(x=>x.missionId==10001).status==MissionStatus.MISSION_STATUS_COMPLETED,"native deck challenge status becomes claimable");
            uint clears=p.Missions.DailyCounters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_CLEAR_PVE);
            await peer.Request<GsNormalBattleEndConfirmOk>(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,new GsNormalBattleEndConfirmRequest{stageId=26,difficulty=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,timeStamp=started.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,cardDieCount=0});
            Check(p.Missions.DailyCounters[BscTable.CHECK_TYPE.MCT_CLEAR_PVE]==clears+1,"successful real battle advances PvE daily progress");
            for(int i=1;i<10;i++)await peer.Request<GsNormalBattleSkipOk>(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,new GsNormalBattleSkipRequest{stageId=26,difficulty=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,skipCount=1});
            Check(p.Missions.DailyCounters[BscTable.CHECK_TYPE.MCT_CLEAR_PVE]==10,"sweeps contribute their exact count to adventure daily task");
            var adventureDaily=mc.Rows[106];uint dice=p.Missions.DicePoints;
            reward=await peer.Claim(106);Check(p.Missions.DicePoints==dice+adventureDaily.RewardDicePoint&&reward.rewardItemInfoList.Single().itemID==adventureDaily.RewardId[1]&&reward.rewardExp==adventureDaily.RewardQuantity[0],"original dice reward is saved alongside the native experience and item rewards");
            before=State(p);await peer.Claim(106,true);Check(before==State(p),"dice points are not duplicated by reward receipt replay");
            await peer.Claim(10001);Check(p.Missions.Claimed.ContainsKey(10001),"deck challenge original reward can be claimed");
            var guild=new LocalGuild{Id=1,Name="浠诲姟楠岃瘉",Level=1,Master=p.Uid,Created=ProtocolRouter.Now};guild.Members[p.Uid]=new(){Joined=ProtocolRouter.Now};p.FoundedGuilds.Add(guild);p.Guild.GuildId=1;server.Store.Save();
            list=await peer.List();Check(list.list.Count(x=>mc.Rows[x.missionId].IconFlag=="HUDICONS_GUILD")==2,"joined guild exposes two original level-matched guild daily objectives");
            before=State(p);await peer.Bad(512);Check(before==State(p),"unimplemented guild battles cannot be bypassed for rewards");
            var rewarded=await peer.Request<GsGetRewardedMissionListOk>(GameServerCommand.GS_GET_REWARDED_MISSION_LIST_REQUEST,new GsGetRewardedMissionListRequest());
            Check(rewarded.list.Any(x=>x.missionId==10001)&&rewarded.list.All(x=>x.rewardedTime>0),"claimed collection challenges and reward timestamps are returned for native stamps");
            p.Missions.ExtraFields=new(){["futureMissionState"]=JsonSerializer.SerializeToElement(new{preserved=true})};permanent=new(p.Missions.Claimed);server.Store.Save();
        }
        await using(var server=new LocalRuntime(root,save,0,0,0)) {
            using var peer=new Peer(root);await peer.Login(server);var p=server.Store.Find(peer.Key.uid)!;var list=await peer.List();
            Check(permanent.All(x=>p.Missions.Claimed.GetValueOrDefault(x.Key)==x.Value),"permanent rewards survive server restart");
            Check(p.Missions.ExtraFields!.ContainsKey("futureMissionState"),"unknown future mission fields survive save reload");
            Check(p.Missions.DicePoints==100,"earned dice-point balance survives server restart");
            Check(list.list.All(x=>!permanent.ContainsKey(x.missionId)),"relogin does not reissue claimed permanent tasks");
        }
        ulong At(int hour,int minute=0)=>(ulong)new DateTimeOffset(2026,10,30,hour,minute,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        var state=new LocalMissionState();ProtocolRouter.RefreshMissionDay(state,30,At(0)-60);state.DailyClaimed[1]=At(3,59);state.Claimed[3000]=At(3,59);state.DailyCounters[BscTable.CHECK_TYPE.MCT_GACHA]=5;
        ProtocolRouter.RefreshMissionDay(state,31,At(0));Check(state.DailyClaimed.Count==0&&state.DailyCounters.Count==0&&state.Claimed.Count==1&&state.DailyLevel==31,"00:00 UTC+8 boundary resets dailies and keeps permanent state");
        var meal=mc.Rows[33];Check(!ProtocolRouter.MissionTimeOpen(meal,At(6,59),true)&&ProtocolRouter.MissionTimeOpen(meal,At(7),true)&&!ProtocolRouter.MissionTimeOpen(meal,At(12),true),"timed stamina task enforces original meal interval");
        Check(hashes.All(x=>File.Exists(x.Key)&&x.Value==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key)))),"production profiles remain untouched");
        File.WriteAllText(Path.Combine(root,"evidence/missions-test.json"),JsonSerializer.Serialize(new{passed=true,checks,save,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS: {checks.Count} mission restoration and reward persistence checks.");
    }
}
