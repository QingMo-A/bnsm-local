using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
internal static class RankDeckRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","rank-deck-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        string account="";List<ulong> saved=[];uint savedRank=0;
        await using(var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(r);var p=r.Store.All().Single();account=p.Account;using var peer=new Peer(root);await peer.Login(r,account);
            string State()=>JsonSerializer.Serialize(new{p.Competitive,p.Decks,p.Gold,p.CashPoint,p.Inventory,p.ActiveStage});
            foreach(int count in new[]{3,10,15}){
                var slots=Enumerable.Repeat(0UL,15).ToList();slots[0]=1;slots[5]=2;slots[10]=3;int next=4;
                for(int i=0;i<15&&next<=count;i++)if(slots[i]==0)slots[i]=(ulong)next++;
                var req=new GsUpdateInfiniteArenaUserDeckInfoRequest{infiniteArenaDeckInfo=new()};req.infiniteArenaDeckInfo.userDeck.AddRange(slots);
                var reply=await peer.Request<GsUpdateInfiniteArenaUserDeckInfoOk>(GameServerCommand.GS_UPDATE_INFINIT_ARENA_DECK_INFO_REQUEST,req);
                Check(reply.userDeckInfo.userDeck.SequenceEqual(slots)&&p.Decks[56].SequenceEqual(slots),"Save all three teams and empty slot positions with "+count+" cards");
                string before=State();await peer.Request<GsUpdateInfiniteArenaUserDeckInfoOk>(GameServerCommand.GS_UPDATE_INFINIT_ARENA_DECK_INFO_REQUEST,req,retry:true);Check(State()==before,"Deck save retry is idempotent for "+count+" cards");saved=slots;
            }
            foreach(string bad in new[]{"emptyTeam","duplicate","training"}){
                var slots=saved.ToList();if(bad=="emptyTeam")for(int i=5;i<10;i++)slots[i]=0;if(bad=="duplicate")slots[5]=slots[0];if(bad=="training")p.TrainingSlots[0]=new(){CardSequence=slots[0],EndTime=ProtocolRouter.Now+60};
                var req=new GsUpdateInfiniteArenaUserDeckInfoRequest{infiniteArenaDeckInfo=new()};req.infiniteArenaDeckInfo.userDeck.AddRange(slots);string before=State();
                await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_INFINIT_ARENA_DECK_INFO_REQUEST,req,success:false);Check(State()==before,"Rejected "+bad+" deck preserves saved teams and currencies");p.TrainingSlots.Clear();
            }
            var arenas=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());var first=arenas.enemyList.Take(3).ToArray();
            Check(first[0].enemyRank<first[1].enemyRank&&first[1].enemyRank<first[2].enemyRank&&first[2].enemyRank<10000,"Strong, matched and easy choices have increasing ranks over distinct jump bands");
            Check(first[1].enemyRank-first[0].enemyRank!=first[2].enemyRank-first[1].enemyRank,"Displayed opponent spacing is irregular");
            var repeated=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());Check(WireCodec.Serialize(arenas).SequenceEqual(WireCodec.Serialize(repeated)),"Repeated preview keeps offered rank and identity stable");
            var offered=first[1];var start=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=offered.enemyUid});
            Check(start.enemy.enemyRank==offered.enemyRank&&p.Competitive.OpponentRank==offered.enemyRank,"Actual start captures the exact displayed opponent rank");
            var end=new GsUpdateArenaBattleResultRequest{targetUid=offered.enemyUid,battleResult=0,battleHistory=new(),battleEndSyncData=new()};end.useCardSeq.AddRange(p.Competitive.Cards);
            string beforeResult=State();await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end,success:false);Check(State()==beforeResult,"No rank advancement before validated replay");
            var c=p.Competitive;var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),c.Enemy)!;
            var replay=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP,winTeamIndex=0};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=c.Opponent});uint index=0;
            foreach(var seq in c.Cards)replay.battleCardInfo.Add(new(){uid=p.Uid,teamIndex=0,cardId=p.CardIds[(int)seq-1],cardSlotIndex=index++});index=0;foreach(var card in enemy.cardInfoList)replay.battleCardInfo.Add(new(){uid=c.Opponent,teamIndex=1,cardId=card.cardId,cardSlotIndex=index++});replay.battleProcessInfo.Add(new());
            byte[] compressed;using(var output=new MemoryStream()){using(var z=new ZLibStream(output,CompressionLevel.Fastest,true))z.Write(WireCodec.Serialize(replay));compressed=output.ToArray();}
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)compressed.Length,firstSendDataFlag=true,replayData=compressed});
            var result=await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end);savedRank=result.currentRank;
            Check(result.currentRank==offered.enemyRank&&result.LastRewardRank==10000,"Defeating higher-ranked opponent takes their displayed place");string after=State();await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end,retry:true);Check(State()==after,"Settlement retry cannot advance again or duplicate reward");r.Store.Save();
        }
        await using(var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=r.Store.All().Single();using var peer=new Peer(root);await peer.Login(r,account);
            var deck=await peer.Request<GsGetInfiniteArenaUserDeckInfoOk>(GameServerCommand.GS_GET_INFINIT_ARENA_DECK_INFO_REQUEST,new GsGetInfiniteArenaUserDeckInfoRequest());Check(deck.userDeckInfo.userDeck.SequenceEqual(saved),"All fifteen defence positions survive server restart and native refresh");Check(p.Competitive.Records[12].Rank==savedRank,"Opponent-position advancement survives restart");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/rank-deck-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("RANK_DECK_TEST_OK "+checks.Count);
    }
}
