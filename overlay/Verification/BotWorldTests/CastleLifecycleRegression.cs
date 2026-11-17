using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class CastleLifecycleRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","castle-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);Console.WriteLine("PASS "+text);}
        uint savedGold=0,savedStamina=0,savedSearches=0;ulong retired=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;var tables=new CompetitiveCatalog(root);var map=tables.CastleMaps.First(x=>x.Prob>0);p.Competitive.HomeMap=(uint)map.MapId;
            foreach(var row in map.SpotList.Where(x=>x.CastleId>0).Take((int)c.Numbers[BscTable.NUMBER_TYPE.CASTLE_ATTACK_CHECK_HAVE_CASTLE_COUNT]))p.Competitive.Castles[map.MapId+"/"+row.SpotId]=new(){Map=(uint)map.MapId,Spot=(uint)row.SpotId,Status=1,WealthStamp=ProtocolRouter.Now};runtime.Store.Save();
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.ActiveStage,p.ActiveBattleStamp,p.Competitive,p.Inventory,p.CardStates,p.EquipmentItems});
            var search=new GsGetCastleBattleAttackMatchRequest();uint beforeGold=p.Gold;var price=c.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RETRY_SEARCH_CASTLE_BATTLE_ENEMY,1,true);
            var found=await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,search);
            Check(found.attackMatchTryCount==1&&p.Competitive.CastleTarget==found.targetUserInfo.uid&&p.Gold==beforeGold-(uint)(price?.Value??0),"Search chooses an actual resident and charges exactly its displayed original price");
            string before=State();await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,search,retry:true);Check(State()==before,"Search retry preserves target, count and balances");
            p.Gold=0;uint count=p.Competitive.CastleSearches;while(c.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RETRY_SEARCH_CASTLE_BATTLE_ENEMY,(int)p.Competitive.CastleSearches+1,true)?.Value==0&&p.Competitive.CastleSearches<30)await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,new GsGetCastleBattleAttackMatchRequest());
            before=State();await peer.Request<FailPacket>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,new GsGetCastleBattleAttackMatchRequest(),success:false);Check(State()==before,"Insufficient search gold cannot change target or count");p.Gold=beforeGold;
            var tile=map.SpotList.First(x=>x.CastleId>0);uint mid=(uint)map.MapId,sid=(uint)tile.SpotId;
            var inspect=new GsGetTargetCastleDefenceDeckInfoRequest{mapId=mid,spotId=sid};before=State();var defence=await peer.Request<GsGetTargetCastleDefenceDeckInfoOk>(GameServerCommand.GS_GET_TARGET_CASTLE_DEFENCE_DECK_INFO_REQUEST,inspect);Check(defence.defenceDeckCardInfoList.Count==5&&State()==before,"Defence preview is a pure read and contains the resident's legal five-card equipment");
            var encounter=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=sid,myCastleFlag=false});
            Check(encounter.stamp!=0&&encounter.cards.Count==5&&encounter.enemyInfo.uid==p.Competitive.CastleTarget,"Attack preparation snapshots enemy and a monotonic encounter token");
            var castle=tables.Castles.Single(x=>x.Id==tile.CastleId);
            var slots=Enumerable.Range(1,p.CardIds.Count).Select(x=>(ulong)x).Where(seq=>{var card=c.Cards.Single(x=>x.Id==p.CardIds[(int)seq-1]);return (castle.FactionCond==BscTable.CARD_FACTION.FACTION_ALL||card.Faction==castle.FactionCond)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_FORCE||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_PHYSICAL||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_PHYSICAL);}).Take(1).ToList();
            Check(slots.Count>0,"Fixture selects a roster satisfying the native castle restrictions");
            var start=new LocalRankCombatRequest{operation=4,mode=18,stamp=encounter.stamp};start.cards.AddRange(slots);uint stamina=p.Stamina,attacks=p.Competitive.CastleAttacks;
            var started=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);var expense=c.Expenses.FindTryTimeData(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_CASTLE_BATTLE_ATTACK_SAME_USER,1);
            Check(started.stamp==encounter.stamp&&p.Stamina==stamina-(expense?.StaminaValue??castle.FirstConsumeStamina)&&p.Competitive.CastleAttacks==attacks+1&&started.castlePlay.todayAttackCount==p.Competitive.CastleAttacks,"Start charges admission and one attack exactly once and returns canonical UI counters");
            before=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start,retry:true);Check(State()==before,"Start retry cannot consume admission or ammunition twice");
            await peer.Request<GsGetTargetCastleDefenceDeckInfoOk>(GameServerCommand.GS_GET_TARGET_CASTLE_DEFENCE_DECK_INFO_REQUEST,inspect);Check(State()==before,"Reading enemy defence during battle cannot replace its token or roster");
            var stale=new LocalRankCombatRequest{operation=3,mode=10,stamp=encounter.stamp};await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,stale,success:false);Check(State()==before,"An old ranked cancel cannot clear an active castle battle");
            await peer.Request<FailPacket>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest(),success:false);Check(State()==before,"A map refresh cannot silently abandon a started modern battle");
            var retreat=new LocalRankCombatRequest{operation=12,mode=18,stamp=encounter.stamp};await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat);
            Check(p.ActiveStage==null&&p.Competitive.ActiveMode==0&&p.Stamina==started.stamina&&p.Competitive.CastleHistory.Count==0&&p.Competitive.CastleDeadCards.Count==0,"Retreat acknowledges the token, preserves paid admission and invents no defeat, casualties or reward");
            before=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat,retry:true);Check(State()==before,"Retreat retry is idempotent");
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start,retry:true,success:false);Check(State()==before,"A cached start cannot resurrect a retreated battle");
            var next=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=sid,myCastleFlag=false});Check(next.stamp>encounter.stamp,"Reentry receives a strictly newer encounter token");
            var second=new LocalRankCombatRequest{operation=4,mode=18,stamp=next.stamp};second.cards.AddRange(slots);await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,second);before=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat,retry:true,success:false);Check(State()==before,"A late retreat receipt cannot affect the newer battle");
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=11,mode=18,stamp=encounter.stamp,castleResult=new(){mapId=mid,spotId=sid}},success:false);Check(State()==before,"An old settlement cannot affect the newer battle");
            var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),p.Competitive.Enemy)!;var replay=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_CASTLE_BATTLE,winTeamIndex=0};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=enemy.uid});uint slot=0;foreach(ulong seq in slots)replay.battleCardInfo.Add(new(){uid=p.Uid,teamIndex=0,cardId=p.CardIds[(int)seq-1],cardSlotIndex=slot++});slot=0;foreach(var card in enemy.cardInfoList)replay.battleCardInfo.Add(new(){uid=enemy.uid,teamIndex=1,cardId=card.cardId,cardSlotIndex=slot++});replay.battleProcessInfo.Add(new());byte[] compressed;using(var output=new MemoryStream()){using(var z=new ZLibStream(output,CompressionLevel.Fastest,true))z.Write(WireCodec.Serialize(replay));compressed=output.ToArray();}
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_CASTLE_BATTLE,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)compressed.Length,firstSendDataFlag=true,replayData=compressed});
            var result=new GsUpdateCastleBattleResultRequest{mapId=mid,spotId=sid,actionType=CastleBattleActionType.CASTLE_BATTLE_ACTION_ATTACK,battleResult=CastleBattleResultType.CASTLE_BATTLE_RESULT_WIN,cardSlot1=slots[0],battleHistory=new(),battleEndSyncData=new(),keyInfo=new()};
            var settle=new LocalRankCombatRequest{operation=11,mode=18,stamp=next.stamp,castleResult=result};uint paidStamina=p.Stamina,ownStatus=p.Competitive.Castles[mid+"/"+sid].Status;
            var done=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,settle);
            Check(done.castleResult.castleMapSpotInfo.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_ATTACKED&&p.Competitive.Castles[mid+"/"+sid].Status==ownStatus,"Plundering an enemy castle updates its display without modifying the player's same-numbered castle");
            Check(p.Stamina>=paidStamina&&p.Competitive.ActiveMode==0&&p.Competitive.CastleHistory.Count==1,"Validated result clears its battle once without charging admission again");
            before=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,settle,retry:true);Check(State()==before,"Settlement retry grants no duplicate reward or history");
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=sid,myCastleFlag=false},success:false);Check(State()==before,"Already-plundered castle rejects repeated entry during the same visit");
            await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());Check(p.Competitive.CastleTarget==0&&p.Competitive.CastleTargetLooted.Count==0,"Returning to the own map clears the enemy visit context");
            var friend=await peer.Request<GsGetFriendRaidCountOk>(GameServerCommand.GS_GET_FRIEND_RAID_PLAY_COUNT_REQUEST,new GsGetFriendRaidCountRequest());Check(friend.todayFriendRaidCount==(uint)Math.Max(0,(long)runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.MAX_FRIEND_RAID_COUNT]+p.Raids.Bought.GetValueOrDefault(6)-p.Raids.Used.GetValueOrDefault(6)),"Castle auxiliary counter query reports remaining monster entries as required by the native client");
            savedGold=p.Gold;savedStamina=p.Stamina;savedSearches=p.Competitive.CastleSearches;retired=next.stamp;runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.All().Single();using var peer=new Peer(root);await peer.Login(runtime,p.Account);var map=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            Check(p.Gold==savedGold&&p.Stamina==savedStamina&&map.castleBattlePlayInfo.attackMatchTryCount==savedSearches&&p.ActiveStage==null,"Server restart restores balances/search count with no stuck battle");
            var found=await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,new GsGetCastleBattleAttackMatchRequest());var tile=found.castleBattleMapSpotInfoList.First();var match=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=tile.mapId,spotId=tile.spotId,myCastleFlag=false});Check(match.stamp>retired,"Battle tokens remain monotonic after server restart");
            var start=new LocalRankCombatRequest{operation=4,mode=18,stamp=match.stamp};start.cards.AddRange(p.Decks[54]);await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);
            savedGold=p.Gold;savedStamina=p.Stamina;retired=match.stamp;runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.All().Single();Check(p.Competitive.Started&&p.Competitive.Stamp==retired,"An unfinished active attempt is persisted before reconnect recovery");
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            Check(p.ActiveStage==null&&p.Competitive.ActiveMode==0&&p.Gold==savedGold&&p.Stamina==savedStamina&&p.Competitive.CastleHistory.Count==1,"Reconnect abandons the lost attempt without charging again or inventing a settlement");
            var state=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=13,mode=18});
            Check(state.activeBattleStamp==0&&state.coins.gold==p.Gold&&state.stamina==p.Stamina,"Lost-retreat acknowledgement recovery reads authoritative idle state and balances");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/castle-lifecycle-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()}));Console.WriteLine("CASTLE_SERVER_PASSED "+checks.Count);
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);
        if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();var c=new CompetitiveCatalog(root);
        var map=c.CastleMaps.First(x=>x.Prob>0);p.Competitive.HomeMap=(uint)map.MapId;
        int required=(int)Math.Ceiling(runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.CASTLE_ATTACK_CHECK_HAVE_CASTLE_COUNT]);
        foreach(var row in map.SpotList.Where(x=>x.CastleId>0).Take(required))
            p.Competitive.Castles[map.MapId+"/"+row.SpotId]=new(){Map=(uint)map.MapId,Spot=(uint)row.SpotId,Status=1,WealthStamp=ProtocolRouter.Now};
        runtime.Store.Save();
        Console.WriteLine($"Castle fixture map={map.MapId} owned={p.Competitive.Castles.Count} required={required}");
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
        await Task.Delay(TimeSpan.FromMinutes(30));
    }
}
