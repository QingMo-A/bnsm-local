using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;

internal static class ManualPvpRegression
{
    internal static void AuditReplay(string root,string folder)
    {
        var p=JsonSerializer.Deserialize<List<LocalPlayer>>(File.ReadAllText(Path.Combine(folder,"saves/players.json")))!.Single();var c=new GameCatalog(root);
        using var input=new MemoryStream(p.Competitive.Replays.OrderBy(x=>x.Key).Last().Value);using var zip=new ZLibStream(input,CompressionMode.Decompress);using var output=new MemoryStream();zip.CopyTo(output);
        var replay=(PKBattleReplayData)WireCodec.Deserialize(typeof(PKBattleReplayData),output.ToArray())!;var births=replay.battleProcessInfo.SelectMany(x=>x.battleSideDeckRet).ToArray();
        var report=new{playerCards=replay.battleCardInfo.Count(x=>x.teamIndex==0),enemyCards=replay.battleCardInfo.Count(x=>x.teamIndex==1),playerSubstitutions=births.Count(x=>x.targetTeamIndex==0),enemySubstitutions=births.Count(x=>x.targetTeamIndex==1),allEntrantsFullSp=births.All(x=>x.battleCardInfo.cardSpritPoint==(uint)(c.Cards.Single(y=>y.Id==x.battleCardInfo.cardId).MaxSp is >0 and var maxSp?maxSp:100)),winner=replay.winTeamIndex};
        File.WriteAllText(Path.Combine(folder,"actual-replay-audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){CompetitiveRegression.Prepare(runtime);runtime.Store.Save();}
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/manual-pvp-server-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);}
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);var c=runtime.Catalog;
        await peer.Request<GsGetRankBattleRecordOk>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new GsGetRankBattleRecordRequest());
        string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.EquipmentItems,p.Competitive,p.ActiveStage,p.ActiveBattleStamp});
        LocalRankCombatRequest Start(uint mode,List<ulong>? reserves=null)=>new(){operation=1,mode=mode,cards=[1,2,3,4,5],reserveCards=reserves??[6,7,8],manualControlVersion=1};
        foreach(uint mode in new[]{10u,42u}){
            p.Competitive.Records[mode]=new();
            foreach(var reserves in new List<ulong>[] {[6,6],[1],[999999],[6,7,8,9]}){
                string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(mode,reserves),success:false);Check(before==State(),$"{mode}: invalid reserves reject before spending or starting");
            }
            var start=Start(mode);var started=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);
            uint startSequence=start.keyInfo.sequenceNo;
            Check(started.manualControlVersion==1&&started.reserveCards.SequenceEqual(start.reserveCards)&&p.Competitive.ReserveCards.SequenceEqual(start.reserveCards),$"{mode}: validated reserves are returned and bound to the battle");
            Check(started.enemyCards.Count==8&&started.enemyCards.Select(x=>x.slotIndex).SequenceEqual(Enumerable.Range(0,8).Select(x=>(uint)x)),$"{mode}: enemy has five starters and three distinct reserve slots");
            Check(started.enemyCards.Select(x=>c.Cards.Single(y=>y.Id==x.cardId).EvolveFirstAncestor).Distinct().Count()==8,$"{mode}: enemy reserves use different legal card identities");
            Check(peer.LastPayload.Length<8192,$"{mode}: full start snapshot fits the original wire frame");
            var saved=new PlayerStore(Path.Combine(folder,"saves"),c).Find(p.Uid)!;Check(saved.Competitive.ReserveCards.SequenceEqual(start.reserveCards)&&saved.ActiveBattleStamp==started.stamp,$"{mode}: restart can recover the exact active roster");
            string active=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start,retry:true);Check(State()==active,$"{mode}: start retry does not change cards, enemy or admission count");
            var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),p.Competitive.Enemy)!;
            async Task Upload(bool includeReserves,bool success,uint entrySp=0,bool duplicateEntry=false){
                var data=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP,winTeamIndex=0};data.thumbNailInfo.Add(new(){uid=p.Uid});data.thumbNailInfo.Add(new(){uid=p.Competitive.Opponent});
                var sequences=includeReserves?p.Competitive.Cards.Concat(p.Competitive.ReserveCards):p.Competitive.Cards;
                uint slot=0;foreach(ulong seq in sequences){uint id=p.CardIds[(int)seq-1];if(mode==42)id=(uint)new CompetitiveCatalog(root).BalanceCards.Single(x=>c.Cards.Single(y=>y.Id==x.CardId).EvolveFirstAncestor==c.Cards.Single(y=>y.Id==id).EvolveFirstAncestor).CardId;data.battleCardInfo.Add(new(){teamIndex=0,cardId=id,uid=p.Uid,cardSlotIndex=slot<5?slot++:5});}
                slot=0;foreach(var card in enemy.cardInfoList)data.battleCardInfo.Add(new(){teamIndex=1,cardId=card.cardId,uid=p.Competitive.Opponent,cardSlotIndex=slot<5?slot++:5});data.battleProcessInfo.Add(new());
                if(entrySp>0){uint id=data.battleCardInfo.First(x=>x.teamIndex==0&&x.cardSlotIndex==5).cardId;
                    for(int i=0;i<(duplicateEntry?2:1);i++)data.battleProcessInfo[0].battleSideDeckRet.Add(new(){targetTeamIndex=0,targetSlotIndex=(uint)i,battleCardInfo=new(){teamIndex=0,uid=p.Uid,cardId=id,cardSlotIndex=(uint)i,cardSpritPoint=entrySp}});
                }
                using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.Fastest,true))zip.Write(WireCodec.Serialize(data));byte[] bytes=output.ToArray();
                await peer.Request<object>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP,replayData=bytes,currentReplayDataSize=(uint)bytes.Length,totalReplayDataSize=(uint)bytes.Length,firstSendDataFlag=true},success:success);
            }
            await Upload(false,false);Check(p.Competitive.Stamp==started.stamp&&p.Competitive.ReplayWinner==null,$"{mode}: replay omitting selected reserves cannot settle or clear the encounter");
            await Upload(true,false,80);Check(p.Competitive.ReplayWinner==null,$"{mode}: reserve entry below full spirit cannot settle");
            await Upload(true,false,100,true);Check(p.Competitive.ReplayWinner==null,$"{mode}: same reserve cannot enter twice");
            await Upload(true,true);
            var finish=new LocalRankCombatRequest{operation=2,mode=mode,stamp=started.stamp,result=0,cards=[1,2,3,4,5]};await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,finish);
            Check(p.ActiveStage==null&&p.Competitive.ReserveCards.Count==0&&p.Competitive.Records[mode].Battles==1,$"{mode}: complete roster replay settles and clears all active state");
            string settled=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,finish,retry:true);Check(State()==settled,$"{mode}: settlement retry cannot grant rewards or wins twice");
            uint latestSequence=start.keyInfo.sequenceNo;start.keyInfo.sequenceNo=startSequence;
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start,retry:true,success:false);start.keyInfo.sequenceNo=latestSequence;Check(State()==settled,$"{mode}: cached ended start cannot revive a settled battle");
            var next=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(mode));ulong nextStamp=next.stamp;
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=mode,stamp=started.stamp},success:false);Check(p.Competitive.Stamp==nextStamp,$"{mode}: stale retreat cannot clear a newer battle");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=mode,stamp=nextStamp});
            Check(p.Competitive.ActiveMode==0&&p.Competitive.ReserveCards.Count==0&&p.Competitive.Records[mode].Battles==1,$"{mode}: retreat clears reserves without recording a defeat");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(mode));
            if(mode==10)await peer.Request<GsGetRankBattleRecordOk>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new GsGetRankBattleRecordRequest());
            else await peer.Request<GsGetBalanceBattleRecordOk>(GameServerCommand.GS_GET_BALANCE_BATTLE_RECORD_REQUEST,new GsGetBalanceBattleRecordRequest());
            Check(p.ActiveStage==null&&p.Competitive.ReserveCards.Count==0&&p.Competitive.Records[mode].Battles==1,$"{mode}: canonical lobby read repairs interrupted manual combat without a fake defeat");
            Check(p.Activities.Weekly.GetValueOrDefault(mode==10?"MCT_ENTER_RANKING_PVP":"MCT_ENTER_BALANCE_PVP")==1,$"{mode}: retries, retreats and lobby recovery cannot inflate restored event progress");
        }
        string serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/manual-pvp-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,serverCoreSha256,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("MANUAL_PVP_SERVER_PASSED "+checks.Count);
    }
}
