using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Reflection;

internal static class WorldWarLifecycleRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","world-war-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);Console.WriteLine("PASS "+text);}
        string savedCards="";uint savedRevives=0;ulong rewardCutoff=0;uint rewardOwner=0;ulong[] unclaimed=[];
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();var numbers=runtime.Catalog.Numbers;
            numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_ON]=numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_OFF]=0;
            p.Decks[33]=new ulong[5].ToList();runtime.Store.Save();using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string Assets()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.Inventory,p.EquipmentItems,p.Competitive.WorldCards,p.Competitive.WorldRevives,p.Competitive.WorldScores});
            async Task<GsWorldWarReadyOk> Ready()=>await peer.Request<GsWorldWarReadyOk>(GameServerCommand.GS_WORLD_WAR_READY_REQUEST,new GsWorldWarReadyRequest{territoryId=6});
            var preview=await Ready();Check(preview.enemyInfo.cardinfolist.Count==5&&p.ActiveStage==null&&!p.Competitive.Started,"An empty attack deck can open the native editor without admitting a battle");
            string before=Assets();var invalid=await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest(),success:false);
            Check(invalid.errorCode==(uint)ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO&&Assets()==before,"Actual start rejects an empty deck with a specific error and no charges");
            p.Decks[33]=p.Deck.ToList();p.Competitive.WorldCards[1]=new(){Hp=0};preview=await Ready();
            Check(preview.myCardStatusList.Any(x=>x.cardSeq==1&&x.hpRatio==0),"A dead selected card still permits opening the editor and carries its saved state");
            before=Assets();invalid=await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest(),success:false);
            Check(invalid.message.Contains("一统天下")&&invalid.message.Contains("阵亡")&&Assets()==before,"Dead cards are rejected at admission with a useful localized hint");
            await peer.Request<GsReviveWorldWarCardOk>(GameServerCommand.GS_REVIVE_WORLD_WAR_CARD_REQUEST,new GsReviveWorldWarCardRequest());await Ready();
            var key=(SessionKeyInfo)typeof(Peer).GetField("key",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(peer)!;
            uint readySequence=key.sequenceNo;
            async Task<T> Cached<T>(GameServerCommand command,object request,uint sequence,bool success){uint current=key.sequenceNo;try{key.sequenceNo=sequence;return await peer.Request<T>(command,request,retry:true,success:success);}finally{key.sequenceNo=current;}}
            await peer.Request<object>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest());uint startSequence=key.sequenceNo;ulong stamp=p.ActiveBattleStamp;before=Assets();
            await peer.Request<object>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest(),retry:true);
            Check(Assets()==before&&p.ActiveBattleStamp==stamp,"Repeated start acknowledgement does not spend ammunition or replace its token");
            await peer.Request<GsGetWorldWarTerritoryInfoOk>(GameServerCommand.GS_GET_WORLD_WAR_TERRITORY_INFO_REQUEST,new GsGetWorldWarTerritoryInfoRequest{territoryId=6});
            Check(p.ActiveBattleStamp==stamp&&p.Competitive.Started,"Territory refresh does not abandon an admitted world battle");
            await peer.Request<GsGetWorldWarLobbyInfoOk>(GameServerCommand.GS_GET_WORLD_WAR_LOBBY_INFO_REQUEST,new GsGetWorldWarLobbyInfoRequest());before=Assets();
            await Cached<FailPacket>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest(),startSequence,false);
            await Cached<FailPacket>(GameServerCommand.GS_WORLD_WAR_READY_REQUEST,new GsWorldWarReadyRequest{territoryId=6},readySequence,false);
            Check(p.ActiveStage==null&&p.Competitive.ActiveMode==0&&Assets()==before,"Lobby return recovers its abandoned battle and rejects old cached preview/start replies");
            await Ready();await peer.Request<object>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest());
            var state=p.Competitive;var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),state.Enemy)!;
            var replay=new PKBattleReplayData{gameMode=(BscTable.GAME_MODE)44,winTeamIndex=0};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=state.Opponent});
            uint slot=0;foreach(ulong seq in state.Cards)replay.battleCardInfo.Add(new(){teamIndex=0,cardId=p.CardIds[(int)seq-1],cardSlotIndex=slot++,uid=p.Uid});slot=0;
            foreach(var card in enemy.cardInfoList)replay.battleCardInfo.Add(new(){teamIndex=1,cardId=card.cardId,cardSlotIndex=slot++,uid=state.Opponent});replay.battleProcessInfo.Add(new());
            byte[] bytes;using(var output=new MemoryStream()){using(var zip=new ZLibStream(output,CompressionLevel.Fastest,true))zip.Write(WireCodec.Serialize(replay));bytes=output.ToArray();}
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=(BscTable.GAME_MODE)44,totalReplayDataSize=(uint)bytes.Length,currentReplayDataSize=(uint)bytes.Length,replayData=bytes,firstSendDataFlag=true});
            var end=new GsWorldWarEndConfirmRequest{bWin=true,battleHistory=new(),battleEndSyncData=new()};before=Assets();
            await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,end,success:false);Check(Assets()==before&&p.Competitive.Started,"Incomplete survivor data cannot omit dead cards or alter a valid battle");
            foreach(ulong seq in state.Cards)end.myCardStatusList.Add(new(){cardSeq=seq,hpRatio=seq==1?0:.5f,spRatio=seq==1?0:1});
            await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,end);uint endSequence=key.sequenceNo;before=Assets();
            await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,end,retry:true);
            Check(Assets()==before&&p.ActiveStage==null&&p.Competitive.WorldCards[1].Hp==0,"Validated settlement and retry preserve score, casualties and idle state exactly once");
            // The newly dead roster is still editable; only actual admission is forbidden.
            await Ready();before=Assets();await Cached<FailPacket>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,end,endSequence,false);
            Check(Assets()==before&&p.Competitive.ActiveMode==44&&!p.Competitive.Started,"A cached old settlement cannot replace a newer world preparation");
            await peer.Request<GsGetWorldWarLobbyInfoOk>(GameServerCommand.GS_GET_WORLD_WAR_LOBBY_INFO_REQUEST,new GsGetWorldWarLobbyInfoRequest());
            var arena=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{mode=12,operation=1});stamp=p.ActiveBattleStamp;
            await peer.Request<GsGetWorldWarLobbyInfoOk>(GameServerCommand.GS_GET_WORLD_WAR_LOBBY_INFO_REQUEST,new GsGetWorldWarLobbyInfoRequest());
            Check(p.Competitive.ActiveMode==12&&p.ActiveBattleStamp==stamp,"Reading world lobby cannot cancel another mode's admitted battle");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{mode=12,operation=3,stamp=arena.stamp});
            numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_ON]=0;numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_OFF]=24;before=Assets();
            invalid=await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_READY_REQUEST,new GsWorldWarReadyRequest{territoryId=6},success:false);
            Check(invalid.errorCode==(uint)ServerErrorCode.ERROR_WORLD_WAR_BREAK_TIME&&Assets()==before,"The original daily break returns its own error without modifying resources");
            savedCards=JsonSerializer.Serialize(p.Competitive.WorldCards);savedRevives=p.Competitive.WorldRevives;runtime.Store.Save();
            var friend=runtime.Store.GetOrCreate("world-reward-fixture");rewardOwner=p.Uid;
            ulong offset=(32-(ulong)numbers[BscTable.NUMBER_TYPE.WORLDWAR_RESET_TIME])*3600;
            ulong day=(ProtocolRouter.Now+offset)/86400,week=(day+3)/7;
            ulong closeDay=(week+2)*7-3;
            rewardCutoff=closeDay*86400-offset;
            var cutoff=DateTimeOffset.FromUnixTimeSeconds((long)rewardCutoff).ToOffset(TimeSpan.FromHours(8));
            Check(cutoff.DayOfWeek==DayOfWeek.Sunday&&cutoff.Hour==22,"World mail uses the original daily 22:00 and Sunday weekly boundary (UTC+8)");
            var catalog=new CompetitiveCatalog(root);var maps=catalog.Territories.Where(x=>x.MapOpen).OrderBy(x=>x.Id).ToList();
            foreach(var territory in maps){
                p.Competitive.WorldScores[(uint)territory.Id]=new(){Daily=100,Weekly=200};
                friend.Competitive.WorldScores[(uint)territory.Id]=new(){Daily=10,Weekly=20};
            }
            p.Competitive.WorldDay=friend.Competitive.WorldDay=closeDay-1;
            p.Competitive.WorldWeek=friend.Competitive.WorldWeek=week+1;
            var absent=runtime.Store.GetOrCreate("world-reward-inactive");
            runtime.Router.SettlementTick(rewardCutoff-1);
            Check(p.Mailbox.Pending.All(x=>x.Territory==0),"No world reward is issued before its cutoff");
            runtime.Router.SettlementTick(rewardCutoff);
            var issued=p.Mailbox.Pending.Where(x=>x.Territory>0).OrderBy(x=>x.Sequence).ToArray();
            Check(issued.Length==2*maps.Count&&issued.All(x=>x.Rank==1&&x.Time==rewardCutoff&&x.Rewards.Count>0)&&absent.Mailbox.Pending.Count==0,"Original reward groups award each occupied board and exclude players without points");
            Check(friend.Mailbox.Pending.Any(x=>x.Territory>0)&&friend.Mailbox.Pending.Where(x=>x.Territory>0).All(x=>x.Rank==2),"Each territory ranks other players by points at the same cutoff");
            var list=await peer.Request<GsGetRewardInfoOk>(GameServerCommand.GS_GET_REWARD_LIST_REQUEST,new GsGetRewardInfoRequest());
            Check(issued.Length>12&&issued.All(m=>list.rewardList.Any(x=>x.rewardSeq==m.Sequence&&x.rewardID1==m.Territory&&x.rewardID2==1)),"Every world reward arrives in the original multipart mailbox, with territory ID and rank");
            foreach(var mail in issued){
                var claim=new GsReceiveRewardItemDataRequest{rewardSeq=mail.Sequence};
                await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,claim);
                string grant=Assets();await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,claim,retry:true);
                if(Assets()!=grant)throw new Exception("Retry changed delivered world assets "+mail.Territory);
            }
            runtime.Router.SettlementTick(rewardCutoff+86400);
            Check(p.Mailbox.Pending.All(x=>x.Territory==0),"Original daily/weekly attachments can be claimed; retry and later ticks cannot pay twice");
            unclaimed=friend.Mailbox.Pending.Where(x=>x.Territory>0).Select(x=>x.Sequence).ToArray();runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.Find(rewardOwner)!;Check(JsonSerializer.Serialize(p.Competitive.WorldCards)==savedCards&&p.Competitive.WorldRevives==savedRevives,"Restart retains world casualties and recovery consumption");
            runtime.Router.SettlementTick(rewardCutoff+86400);
            Check(p.Mailbox.Pending.All(x=>x.Territory==0)&&runtime.Store.All().Single(x=>x.Account=="world-reward-fixture").Mailbox.Pending.Where(x=>x.Territory>0).Select(x=>x.Sequence).SequenceEqual(unclaimed),"Restart respects paid periods and retains another player's unclaimed rewards");
        }
        string serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant();
        string proofName=typeof(LocalRuntime).Assembly.GetName().Name=="Bnsm.EmbeddedServer"?"world-war-embedded-latest.json":"world-war-server-latest.json";
        File.WriteAllText("I:/BnsmModernWork/Verification/"+proofName,JsonSerializer.Serialize(new{passed=true,checks,folder,serverCoreSha256,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
