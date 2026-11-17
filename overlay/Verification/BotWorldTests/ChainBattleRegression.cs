using ProtoBuf;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class ChainBattleRegression
{
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);
        if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");
        Directory.CreateDirectory(folder);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        CompetitiveRegression.Prepare(runtime);
        var player=runtime.Store.All().Single();
        player.Decks[20]=player.Deck.ToList();
        runtime.Store.Save();
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
        await Task.Delay(TimeSpan.FromMinutes(25));
    }

    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/chain-server-"+Guid.NewGuid().ToString("N");
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);}
        uint uid=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false))
        {
            CompetitiveRegression.Prepare(runtime);
            var player=runtime.Store.All().Single();uid=player.Uid;
            player.Decks[20]=player.Deck.ToList();
            using var peer=new Peer(root);await peer.Login(runtime,player.Account);
            string State()=>JsonSerializer.Serialize(new{player.ChainBattle,player.Competitive,player.Gold,player.CashPoint,player.ArenaPoint,player.Inventory,player.ActiveStage,player.ActiveBattleStamp});
            async Task<GsGetChainBattleCardInfoOk> Preview()=>await peer.Request<GsGetChainBattleCardInfoOk>(GameServerCommand.GS_GET_CHAIN_BATTLE_CARD_INFO_REQUEST,new GsGetChainBattleCardInfoRequest());
            async Task<LocalChainBattleStartOk> Start()=>await peer.Request<LocalChainBattleStartOk>(GameServerCommand.GS_CHAIN_BATTLE_START_REQUEST,new GsChainBattleStartRequest{stepId=player.ChainBattle.Step,deckid=20});
            async Task Replay(bool won)
            {
                var battle=player.Competitive;
                var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),battle.Enemy)!;
                var replay=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_CHAINBATTLE_PVP,winTeamIndex=won?0u:1u};
                replay.thumbNailInfo.Add(new(){uid=player.Uid});replay.thumbNailInfo.Add(new(){uid=enemy.uid});
                uint slot=0;
                foreach(ulong seq in battle.Cards)replay.battleCardInfo.Add(new(){teamIndex=0,cardSlotIndex=slot++,cardId=player.CardIds[(int)seq-1],uid=player.Uid});
                slot=0;
                foreach(var row in enemy.cardInfoList)replay.battleCardInfo.Add(new(){teamIndex=1,cardSlotIndex=slot++,cardId=row.cardId,uid=enemy.uid});
                replay.battleProcessInfo.Add(new());
                using var output=new MemoryStream();
                using(var stream=new ZLibStream(output,CompressionLevel.Fastest,true))stream.Write(WireCodec.Serialize(replay));
                byte[] bytes=output.ToArray();
                await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest
                {
                    gameMode=BscTable.GAME_MODE.MODE_CHAINBATTLE_PVP,firstSendDataFlag=true,
                    replayData=bytes,totalReplayDataSize=(uint)bytes.Length,currentReplayDataSize=(uint)bytes.Length
                });
            }
            LocalChainBattleEndRequest End(ulong stamp,bool won)
            {
                var end=new LocalChainBattleEndRequest{stamp=stamp,endType=won?NormalBattleEndType.NORMAL_BATTLE_SUCCESS:NormalBattleEndType.NORMAL_BATTLE_FAIL,battleEndSyncData=new(),battleHistory=new()};
                foreach(ulong seq in player.Competitive.Cards)end.myCardInfoList.Add(new(){cardSeq=seq,hpRatio=won?.7f:0,spRatio=won?22:0,spBubbleCount=won?1u:0u});
                var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),player.Competitive.Enemy)!;
                foreach(var row in enemy.cardInfoList)end.enemyCardInfoList.Add(new(){cardSeq=row.cardId,hpRatio=won?0:.5f,spRatio=won?0:17});
                return end;
            }
            var record=await peer.Request<GsGetChainBattleRecordOk>(GameServerCommand.GS_GET_CHAIN_BATTLE_RECORD_REQUEST,new GsGetChainBattleRecordRequest());
            Check(record.chainBattleRecord.id==6&&!record.chainBattleRecord.continueFlag&&record.chainBattleRecord.todayResetCount==0,"Old profiles enter the original level bracket without spending a run");
            var reset=new GsResetChainBattleRecordRequest();
            await peer.Request<GsResetChainBattleRecordOk>(GameServerCommand.GS_RESET_CHAIN_BATTLE_RECORD_REQUEST,reset);
            Check(player.ChainBattle.Step==1&&player.ChainBattle.Used==1,"Reset begins the daily run at the original first step");
            string before=State();await peer.Request<GsResetChainBattleRecordOk>(GameServerCommand.GS_RESET_CHAIN_BATTLE_RECORD_REQUEST,reset,retry:true);
            Check(State()==before,"Reset acknowledgement retries are idempotent");
            await peer.Request<FailPacket>(GameServerCommand.GS_RESET_CHAIN_BATTLE_RECORD_REQUEST,new GsResetChainBattleRecordRequest(),success:false);
            Check(State()==before,"Daily exhaustion does not reset progress");
            var preview=await Preview();byte[] opponent=WireCodec.Serialize(preview.battleEnemyInfo);
            var repeated=await Preview();
            Check(opponent.SequenceEqual(WireCodec.Serialize(repeated.battleEnemyInfo)),"Repeated deck previews retain the same opponent");
            var savedDeck=player.Decks[20].ToList();player.Decks[20]=[0,0,0,0,0];before=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_START_REQUEST,new GsChainBattleStartRequest{stepId=1,deckid=20},success:false);
            Check(State()==before,"Empty decks reject before battle admission");player.Decks[20]=savedDeck;
            var start=await Start();var end=End(start.stamp,true);before=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end,success:false);
            Check(State()==before,"Unverified battle results cannot grant rewards");
            await Replay(true);end.stamp++;before=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end,success:false);
            Check(State()==before,"Mismatched attempt stamps reject atomically");end.stamp--;
            uint gold=player.Gold;
            var finish=await peer.Request<GsChainBattleEndConfirmOk>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end);
            Check(player.Gold==gold+10000&&finish.chainBattleRecord.stepId==2&&player.ActiveStage==null,"Verified victory grants the original stage reward and advances once");
            before=State();await peer.Request<GsChainBattleEndConfirmOk>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end,retry:true);
            Check(State()==before,"Settlement retries preserve rewards, HP, SP and progress");
            preview=await Preview();
            Check(preview.myCardInfoList.All(x=>x.hpRatio==.7f&&x.spRatio==22&&x.spBubbleCount==1),"Native deck re-entry carries forward HP, spirit and ultimate bubbles");
            start=await Start();before=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end,retry:true,success:false);
            Check(State()==before,"An old successful settlement cannot replace a newer active battle");
            var retreat=new LocalRankCombatRequest{operation=10,mode=16,stamp=start.stamp};
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat);
            Check(player.ActiveStage==null&&player.ChainBattle.Step==2&&player.ChainBattle.Battles==1,"Retreat preserves the checkpoint without a fabricated defeat or reward");
            preview=await Preview();start=await Start();await Replay(false);var loss=End(start.stamp,false);
            await peer.Request<GsChainBattleEndConfirmOk>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,loss);
            Check(player.ChainBattle.Step==2&&player.ChainBattle.Continuing,"Defeat allows another surviving owned team to challenge the same checkpoint");
            preview=await Preview();
            Check(preview.enemyCardInfoList.All(x=>x.hpRatio==.5f)&&preview.myCardInfoList.All(x=>x.hpRatio==0),"Defeat retains enemy wounds and marks the defeated owned cards unusable");
            before=State();await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_START_REQUEST,new GsChainBattleStartRequest{stepId=2,deckid=20},success:false);
            Check(State()==before,"Dead cards cannot be submitted through the original deck request");
            player.Decks[20]=Enumerable.Range(1,player.CardIds.Count).Select(x=>(ulong)x).Where(x=>!player.ChainBattle.Cards.ContainsKey(x)).Take(5).ToList();
            for(int iteration=0;iteration<20&&player.ChainBattle.Step<=13;iteration++)
            {
                uint step=player.ChainBattle.Step;
                if(step is 4 or 8 or 12)
                {
                    uint oldGold=player.Gold;
                    var shuffle=new GsShuffleChainBattleBuffRequest();
                    var rolled=await peer.Request<GsShuffleChainBattleBuffOk>(GameServerCommand.GS_SHUFFLE_CHAIN_BATTLE_BUFF_REQUEST,shuffle);
                    Check(rolled.chainBattleRecord.buffShuffleCount==1&&player.Gold<oldGold,"Original buff reroll expense is paid at step "+step);
                    before=State();await peer.Request<GsShuffleChainBattleBuffOk>(GameServerCommand.GS_SHUFFLE_CHAIN_BATTLE_BUFF_REQUEST,shuffle,retry:true);
                    Check(State()==before,"Buff reroll retry cannot charge or reroll twice at step "+step);
                    await peer.Request<GsBuyChainBattleBuffOk>(GameServerCommand.GS_BUY_CHAIN_BATTLE_BUFF_REQUEST,new LocalChainBattleBuffRequest());
                    Check(player.ChainBattle.Step==step+1&&player.ChainBattle.Buff!=0,"Scheduled native buff confirmation advances at step "+step);
                    continue;
                }
                preview=await Preview();
                byte faction=player.ChainBattle.Factions[step];
                player.Decks[20]=Enumerable.Range(1,player.CardIds.Count).Select(x=>(ulong)x).Where(x=>!player.ChainBattle.Cards.TryGetValue(x,out var card)||card.Hp>0).Where(x=>faction==3||(byte)runtime.Catalog.RequireCard(player.CardIds[(int)x-1]).Faction==faction).Take(5).ToList();
                Check(player.Decks[20].Count>0,"Original faction restriction has an eligible survivor at step "+step);
                if(faction!=3)
                {
                    var valid=player.Decks[20];
                    ulong other=Enumerable.Range(1,player.CardIds.Count).Select(x=>(ulong)x).First(x=>(byte)runtime.Catalog.RequireCard(player.CardIds[(int)x-1]).Faction!=faction);
                    player.Decks[20]=[other];before=State();
                    await peer.Request<FailPacket>(GameServerCommand.GS_CHAIN_BATTLE_START_REQUEST,new GsChainBattleStartRequest{stepId=step,deckid=20},success:false);
                    Check(State()==before,"Server enforces the native faction restriction at step "+step);player.Decks[20]=valid;
                }
                start=await Start();await Replay(true);end=End(start.stamp,true);
                await peer.Request<GsChainBattleEndConfirmOk>(GameServerCommand.GS_CHAIN_BATTLE_END_CONFIRM_REQUEST,end);
            }
            Check(player.ChainBattle.Step==14&&!player.ChainBattle.Continuing&&player.ChainBattle.FirstClears.Contains(6)&&player.ChainBattle.Wins==10,"Ten original battles and three buff stages complete with a persistent first-clear ledger");
            var history=await peer.Request<GsGetChainBattleHistoryOk>(GameServerCommand.GS_GET_CHAIN_BATTLE_HISTORY_REQUEST,new GsGetChainBattleHistoryRequest());
            Check(history.chainBattleHistory.Count==11&&history.chainBattleHistory.All(x=>x.replaySeq!=0),"Native history retains validated wins, losses and replay identities");
            var rank=await peer.Request<GsGetRankingBoardChainBattleDetailInfoOk>(GameServerCommand.GS_GET_RAINKING_BOARD_CHAIN_BATTLE_DETAIL_REQUEST,new GsGetRankingBoardChainBattleDetailInfoRequest{targetUid=player.Uid});
            Check(rank.targetUid==player.Uid&&player.ChainBattle.BestWins==10,"Chain ranking detail reads actual local progress");
            player.ChainBattle.Day--;runtime.Store.Save();
            var swept=await peer.Request<GsSkipChainBattleOk>(GameServerCommand.GS_SKIP_CHAIN_BATTLE_REQUEST,new GsSkipChainBattleRequest());
            Check(swept.chainBattleRecord.stepId==14&&swept.chainBattleRecord.todayResetCount==1&&swept.earnedItemList.Count>0&&!swept.chainBattleRecord.canRecieveFirstClearReward,"Next-day sweep grants completed original checkpoints without repeating first-clear rewards");
            runtime.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false))
        {
            var player=restarted.Store.Find(uid)!;
            Check(player.ChainBattle.Step==14&&player.ChainBattle.Used==1&&player.ChainBattle.FirstClears.Contains(6),"Run progress, daily usage and first-clear receipts survive full host restart");
        }
        string core=typeof(LocalRuntime).Assembly.Location;
        File.WriteAllText("I:/BnsmModernWork/Verification/chain-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Chain regression passed "+checks.Count+" checks");
    }

    internal static void Audit(string root)
    {
        T Read<T>(string name){using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
        var data=Read<BscTable.ChainBattleDataTable>("ChainBattleDataTable").List;
        var rewards=Read<BscTable.ChainBattleRewardDataTable>("ChainBattleRewardDataTable").List;
        var catalog=new GameCatalog(root);
        Console.WriteLine(JsonSerializer.Serialize(new{data=data.Select(x=>new{x.Id,x.PcLevelMin,x.PcLevelMax,x.FirstRewardIdType,x.FirstRewardId,x.FirstRewardQuantity,x.PvpTitleId,
            steps=x.StepDataList.Select(s=>new{s.Step,s.StepType,s.BuffSetId,s.Faction,s.FactionProb,s.MatchNumber,s.BattleCount,s.RewardIdType,s.RewardId,s.RewardQuantity,s.Prob})}),
            rewards=rewards.Select(x=>new{x.WinCount,rows=x.RewardList.Select(r=>new{r.LvMin,r.LvMax,r.RewardIdType,r.RewardId,r.Quantity,r.Prob,r.PvpTitleId})}),
            numbers=catalog.Numbers.Where(x=>x.Key.ToString().Contains("CHAIN"))},new JsonSerializerOptions{WriteIndented=true}));
    }
}
