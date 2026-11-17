using System.Net.Sockets;
using System.Text.Json;

internal static class BattleExitTest
{
    private sealed class Peer(string root):IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort command,object body)
        {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(body),true));
            using var timeout=new CancellationTokenSource(10000);var packet=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(packet.Command,packet.Payload);
        }
        public async Task Login(LocalRuntime runtime)
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",runtime.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var packet=await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),packet.Payload)!;Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",runtime.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,uint? sequence=null)
        {
            uint current=Key.sequenceNo;Key.sequenceNo=sequence??(current+1);body.GetType().GetField("keyInfo")!.SetValue(body,Key);
            var packet=await Send(socket,(ushort)command,body);Key.sequenceNo=Math.Max(current,Key.sequenceNo);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)packet.Command).ToString()!=expected)throw new Exception(expected+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),packet.Payload)!).message);
            var result=(T)WireCodec.Deserialize(typeof(T),packet.Payload)!;
            if(!success&&result is FailPacket failure&&failure.errorCode!=(uint)ServerErrorCode.ERROR_INVALID_ID)throw new Exception("Unexpected failure "+failure.errorCode);
            return result;
        }
        public void Dispose()=>socket.Dispose();
    }
    internal static void Prepare(LocalRuntime runtime)
    {
        RaidTest.Prepare(runtime);var p=runtime.Store.GetOrCreate("local-player");
        p.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)};p.ChallengeTower=new(){Day=p.Wander.Day,Week=(p.Wander.Day+3)/7};runtime.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated battle-exit fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            Prepare(runtime);var p=runtime.Store.GetOrCreate("local-player");var c=runtime.Catalog;
            var banned=c.TowerBans.Single(x=>x.Id==1).CTBanCardIdInfoList.Select(x=>x.BanCardId).ToHashSet();
            var champions=c.UsableHeroes.Where(x=>!banned.Contains(x.EvolveFirstAncestor)&&x.Talent>=BscTable.CARD_TALENT.CARD_TALENT_SS).GroupBy(x=>x.EvolveFirstAncestor).Select(g=>g.OrderByDescending(x=>x.Rank).First()).OrderByDescending(x=>x.Rank).Take(5);
            foreach(var card in champions) {
                p.CardIds.Add((uint)card.Id);ulong seq=(ulong)p.CardIds.Count;
                var state=new LocalCardState{Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=10,ActiveSkills=[99,99],PassiveSkills=[99,99,99,99,99],GemIds=[120101010,120301010,120501010]};p.CardStates[seq]=state;
                var weapon=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON&&x.Class==card.Class&&x.CardId==0&&x.CardCharacterType==BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE).OrderByDescending(x=>x.Step).First();
                var gear=new LocalOwnedEquipment{ItemId=(uint)weapon.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(gear);state.WeaponSequence=gear.Sequence;
            }
            p.Decks[22]=[6,7,8,9,10];p.Decks[25]=[6,7,8,9,10];runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        string save=Path.Combine(root,"verification/battle-exit",Guid.NewGuid().ToString("N"));ulong lastStamp=0;
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false)) {
            Prepare(runtime);using var peer=new Peer(root);await peer.Login(runtime);var p=runtime.Store.Find(peer.Key.uid)!;
            string Progress()=>JsonSerializer.Serialize(new{p.Level,p.Exp,p.Gold,p.CashPoint,p.Stamina,p.Inventory,p.CardIds,p.CardStates,p.EquipmentItems,p.Wander,p.ChallengeTower.Points,p.ChallengeTower.UsedEntries,p.ChallengeTower.Areas,p.ChallengeTower.DeadCards,p.ChallengeTower.DailyFloor});
            async Task Bad(GameServerCommand cmd,object body,string text,uint? sequence=null){string before=Progress();await peer.Request<FailPacket>(cmd,body,false,sequence);Check(before==Progress(),text);}
            var enter=await peer.Request<GsEnterWanderBattleOk>(GameServerCommand.GS_ENTER_WANDER_BATTLE_REQUEST,new GsEnterWanderBattleRequest{difficulty=BscTable.WANDER_BATTLE_DIFFICULTY.WANDER_BATTLE_DIFFICULTY_NORMAL});
            var first=await peer.Request<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1});uint originalSequence=peer.Key.sequenceNo;
            await Bad(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1},"Baseline overlap is rejected while a real battle is active");
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,new GsChallengeStageLobbyRequest());
            Check(p.ActiveStage==1&&p.Raids.ActiveMode==5,"Tower lobby cannot abandon an unrelated wander battle");
            await peer.Request<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest());
            Check(p.ActiveStage==1,"Ordinary game-info polling does not cancel combat");
            string progress=Progress();
            var record=await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            Check(p.ActiveStage==null&&p.Raids.ActiveMode==0&&p.Raids.ActiveStamp==0&&record.wanderBattleRecord.startBattleTime==0,"Returning to wander map releases the unfinished attempt (original bug)");
            Check(progress==Progress(),"Wander retreat gives no rewards, refunds, casualties or progress changes");
            await Bad(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1},"Cached abandoned wander start cannot resurrect its token",originalSequence);
            var second=await peer.Request<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,new GsWanderBattleStartRequest{spotId=1});
            Check(second.startTimeStamp>first.startTimeStamp,"Immediate wander retry receives a new monotonic token");
            var stale=new GsWanderBattleEndConfirmRequest{spotId=1,startTimeStamp=first.startTimeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(first.startTimeStamp%1000),battleEndSyncData=new()};stale.cardInfoList.Add(new(){cardSeq=1,hpRatio=1});
            await Bad(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,stale,"Late abandoned wander settlement cannot reward the new attempt");
            await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            Check(progress==Progress(),"Repeated map return is idempotent and preserves the journey");
            var tower=await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,new GsChallengeStageBattleStartRequest{difficulty=1,stageid=1});uint towerSequence=peer.Key.sequenceNo;
            await peer.Request<GsGetWanderBattleRecordOk>(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest());
            Check(p.ActiveStage==11&&p.ChallengeTower.ActiveStamp==tower.timeStamp,"Wander map cannot abandon an unrelated tower battle");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,new GsChallengeStageBattleStartRequest{difficulty=1,stageid=1},"Tower still rejects overlapping starts");
            progress=Progress();await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,new GsChallengeStageLobbyRequest());
            Check(p.ActiveStage==null&&p.ChallengeTower.ActiveId==0&&p.ChallengeTower.BattleCards.Count==0,"Returning to tower lobby releases the unfinished attempt");
            Check(progress==Progress(),"Tower retreat preserves entries, points, regions and all surviving cards");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,new GsChallengeStageBattleStartRequest{difficulty=1,stageid=1},"Cached abandoned tower start cannot resurrect its token",towerSequence);
            var nextTower=await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,new GsChallengeStageBattleStartRequest{difficulty=1,stageid=1});
            Check(nextTower.timeStamp>tower.timeStamp,"Immediate tower retry receives a new token");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,new GsChallengeStageBattleEndConfirmRequest{difficulty=1,stageid=1,timeStamp=tower.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(tower.timeStamp%1000),battleEndSyncData=new()},"Late abandoned tower settlement cannot reward a newer attempt");
            var won=await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,new GsChallengeStageBattleEndConfirmRequest{difficulty=1,stageid=1,timeStamp=nextTower.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(nextTower.timeStamp%1000),battleEndSyncData=new()});
            Check(won.earnedChallengePoint==15&&p.ChallengeTower.Areas[11]==3&&p.ActiveStage==null,"Tower victory after retreat still awards original rewards once");
            // Simulate a legacy orphaned marker, including a clock moving backward.
            lastStamp=nextTower.timeStamp+100000;p.ChallengeTower.ActiveStamp=lastStamp;p.ChallengeTower.ActiveId=12;p.ChallengeTower.BattleCards=[1];p.ActiveStage=null;p.ActiveBattleStamp=0;runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(runtime);var p=runtime.Store.Find(peer.Key.uid)!;
            Check(p.ActiveStage==null&&p.ChallengeTower.ActiveStamp==0,"Login repairs legacy orphaned battle markers without a save reset");
            var started=await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,new GsChallengeStageBattleStartRequest{difficulty=1,stageid=2});
            Check(started.timeStamp>lastStamp&&p.ChallengeTower.Areas[11]==3,"Restart and clock rollback cannot reuse old tokens or erase cleared regions");
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,new GsChallengeStageLobbyRequest());
            Check(p.ChallengeTower.Areas.Count==1&&p.ChallengeTower.UsedEntries==1&&p.ChallengeTower.Points==15,"Repeated exits preserve the legitimate first-region rewards and daily entry");
        }
        File.WriteAllText(Path.Combine(root,"evidence/battle-exit-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Battle exit: {checks.Count}/{checks.Count}");
    }
}
