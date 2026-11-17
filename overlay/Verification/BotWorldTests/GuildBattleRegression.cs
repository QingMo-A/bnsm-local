using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class GuildBattleRegression
{
    internal static void Prepare(LocalRuntime runtime)
    {
        CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();var bot=runtime.Router.Bots.All().OrderBy(x=>x.Uid).Last();
        var g=new LocalGuild{Id=1,Name="归来门派",Level=1,Emblem=(uint)new GuildCatalog("I:/BnsmModernWork/WindowsPreview").Emblems.First().Id,Master=p.Uid,Created=ProtocolRouter.Now,AttendanceDay=ProtocolRouter.GuildAttendanceDay(ProtocolRouter.Now)};
        g.Members[p.Uid]=new(){Grade=GuildUserGrade.GUILD_USER_GRADE_MASTER,Joined=ProtocolRouter.Now};g.Members[bot.Uid]=new(){IsBot=true,Joined=ProtocolRouter.Now};p.FoundedGuilds.Add(g);p.Guild.GuildId=1;runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith(@"I:\BnsmModernWork\Verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("isolated guild battle fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);if(runtime.Store.All().Count==0)Prepare(runtime);
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);}
        string folder=Path.Combine("I:/BnsmModernWork/Verification","guild-battle-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string account="";uint entries=0,points=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            Prepare(runtime);var p=runtime.Store.All().Single();account=p.Account;runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.GUILD_BATLE_COOLTIME_SEC]=60;var g=p.FoundedGuilds.Single();using var peer=new Peer(root);await peer.Login(runtime,account);
            string State()=>JsonSerializer.Serialize(new{p.Competitive,p.ActiveStage,p.ActiveBattleStamp,p.Guild,p.FoundedGuilds,p.ArenaPoint,p.RankingPoint,p.Gold,p.CashPoint,p.Missions,p.Inventory});
            async Task Bad(LocalRankCombatRequest request,string label){string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,request,success:false);Check(State()==before,label);Console.WriteLine("PASS "+label);}
            var list=await peer.Request<GsSearchGuildBattleEnemyListOk>(GameServerCommand.GS_SEARCH_GUILD_BATTLE_ENEMY_LIST_REQUEST,new GsSearchGuildBattleEnemyListRequest());
            Check(list.enemyList.Count==9&&list.enemyList.Select(x=>x.enemyGuildRankGrade).Distinct().Count()==3,"Three difficulty bands use distinct persistent resident identities");
            var offer=list.enemyList.Last(x=>x.enemyGuildRankGrade==2);var preview=await peer.Request<GsGetGuildBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_GUILD_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetGuildBattleEnemyDeckInfoRequest{targetGuildId=offer.enemyGuildId});
            Check(preview.battleCardInfoList.Count==5&&p.ActiveStage==null&&p.Competitive.Records[29].Used==0,"Opponent preview does not admit, spend entries or ammunition");
            var repeated=await peer.Request<GsSearchGuildBattleEnemyListOk>(GameServerCommand.GS_SEARCH_GUILD_BATTLE_ENEMY_LIST_REQUEST,new GsSearchGuildBattleEnemyListRequest());Check(WireCodec.Serialize(list).SequenceEqual(WireCodec.Serialize(repeated)),"Preview refresh preserves offers and strength");
            var start=new LocalRankCombatRequest{mode=29,operation=1,targetUid=offer.enemyGuildId};start.cards.AddRange(p.Deck);start.cards.AddRange(new ulong[10]);
            var invalid=new LocalRankCombatRequest{mode=29,operation=1,targetUid=offer.enemyGuildId};invalid.cards.AddRange([1UL,1,2,3,4]);await Bad(invalid,"Duplicate roster rejects before admission");
            uint oldLevel=p.Level;p.Level=1;await Bad(start,"Original player unlock remains enforced");p.Level=oldLevel;
            var match=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);Check(match.operation==1&&match.stamp>0&&p.ActiveBattleStamp==match.stamp&&match.entrance.guildBattleEntranceCount==1,"Actual start atomically captures roster, token, entry and cooldown");
            string admitted=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start,retry:true);Check(State()==admitted,"Repeated start does not consume a second entry");
            var info=await peer.Request<GsGetGuildBattleRecordOk>(GameServerCommand.GS_GET_GUILD_BATTLE_RECORD_REQUEST,new GsGetGuildBattleRecordRequest());Check(p.ActiveBattleStamp==match.stamp,"Ordinary guild record refresh cannot cancel an admitted battle");
            var cancel=new LocalRankCombatRequest{mode=29,operation=3,stamp=match.stamp};await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,cancel);Check(p.ActiveStage==null&&p.Competitive.Records[29].Used==1&&g.Battle.Battles==0,"Retreat retains entry/cooldown without a fake defeat or rewards");
            await Bad(start,"Cooldown blocks immediate reentry");var reset=new GsResetPvpBattleCoolTimeRequest{pvpBattleType=BscTable.PVP_BATTLE_TYPE.PVP_BATTLE_GUILDARENA};uint cash=p.CashPoint;await peer.Request<GsResetPvpBattleCoolTimeOk>(GameServerCommand.GS_RESET_PVP_BATTLE_COOLTIME_REQUEST,reset);Check(p.CashPoint<=cash&&p.Competitive.Records[29].CoolResets==1,"Original cooldown reset price and daily counter are used");
            match=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);await Bad(cancel,"Old retreat cannot clear a newer attempt");
            var end=new LocalRankCombatRequest{mode=29,operation=2,targetUid=offer.enemyGuildId,stamp=match.stamp,result=0};end.cards.AddRange(p.Deck);await Bad(end,"Settlement requires a matching uploaded native replay");
            var c=p.Competitive;var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),c.Enemy)!;var replay=new PKBattleReplayData{gameMode=BscTable.GAME_MODE.MODE_GUILD_ARENA_PVP,winTeamIndex=0};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=c.Opponent});replay.battleProcessInfo.Add(new());
            for(int i=0;i<c.GuildReplayCards.Count;i++)replay.battleCardInfo.Add(new(){teamIndex=0,cardSlotIndex=(uint)i,cardId=c.GuildReplayCards[i]});for(int i=0;i<enemy.cardInfoList.Count;i++)replay.battleCardInfo.Add(new(){teamIndex=1,cardSlotIndex=(uint)i,cardId=enemy.cardInfoList[i].cardId});
            using var compressed=new MemoryStream();using(var z=new ZLibStream(compressed,CompressionLevel.Optimal,true))z.Write(WireCodec.Serialize(replay));byte[] bytes=compressed.ToArray();await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=BscTable.GAME_MODE.MODE_GUILD_ARENA_PVP,firstSendDataFlag=true,totalReplayDataSize=(uint)bytes.Length,currentReplayDataSize=(uint)bytes.Length,replayData=bytes});
            uint arena=p.ArenaPoint;var settled=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,end);Check(settled.guildResult!=null&&g.Battle.Wins==1&&g.Battle.Battles==1&&g.Battle.Points>0&&p.ActiveStage==null,"Validated native victory settles guild points, history and clear state");
            Check(p.Missions.Counters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_ENTER_GUILD_BATTLE)==1&&p.Missions.Counters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_WIN_GUILD_BATTLE)==1,"Only completed guild battles advance participation/win objectives");
            admitted=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,end,retry:true);Check(State()==admitted,"Settlement retry never duplicates score, currencies or objectives");
            var histories=await peer.Request<GsGetGuildArenaBattleHistoryOk>(GameServerCommand.GS_GET_GUILD_ARENA_BATTLE_HISTORY_REQUEST,new GsGetGuildArenaBattleHistoryRequest());Check(histories.arenaBattleHistoryList.Count==1&&histories.arenaBattleHistoryList[0].arenaHistoryData.replaySeq==match.stamp,"Native guild history retains the replay identity");
            await peer.Request<GsResetPvpBattleCoolTimeOk>(GameServerCommand.GS_RESET_PVP_BATTLE_COOLTIME_REQUEST,reset);await Bad(start,"Original guild-level daily entry cap is enforced");p.Competitive.Records[29].Used=0; // Isolated refill for the separate volunteer scenario.
            var member=runtime.Router.Bots.Find(g.Members.Keys.Single(x=>x!=p.Uid))!.Profile;uint card=member.CardIds[(int)member.Deck[0]-1];var volunteer=await peer.Request<GsGetVolunteerCardDetailInfoOk>(GameServerCommand.GS_GET_GUILD_BATTLE_VOLUNTEER_CARD_BATTLE_INFO_REQUEST,new GsGetVolunteerCardDetailInfoRequest{volunteerUserUid=member.Uid,volunteerCardID=card});
            var borrow=new LocalRankCombatRequest{mode=29,operation=1,targetUid=offer.enemyGuildId,guildMercenary=volunteer.guildUserBattleDeckCardInfo};borrow.cards.AddRange(p.Deck);borrow.guildMercenary.slotIndex=0;borrow.cards[0]=999999;match=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,borrow);Check(p.Guild.BattleVolunteers.Contains(member.Uid)&&match.enemyCards.Single().uid==member.Uid&&p.Competitive.GuildReplayCards[0]==card,"Same-guild resident volunteer is authoritatively captured at admission");
            await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,end,success:false);Check(p.ActiveBattleStamp==match.stamp,"Old victory cannot replace the newer borrowed attempt");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{mode=29,operation=0});Check(p.ActiveStage==null&&p.Guild.BattleVolunteers.Contains(member.Uid),"Canonical lobby recovery keeps admission and volunteer usage");
            var used=await peer.Request<GsGetGuildBattleVolunteerListOk>(GameServerCommand.GS_GET_GUILD_BATTLE_USED_VOLUNTEER_LIST_REQUEST,new GsGetGuildBattleVolunteerListRequest());Check(used.usedUserUidList.Contains(member.Uid),"Native used-volunteer list mirrors persisted admission");
            entries=p.Competitive.Records[29].Used;points=g.Battle.Points;runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){using var peer=new Peer(root);await peer.Login(runtime,account);var p=runtime.Store.All().Single();var info=await peer.Request<GsGetGuildBattleRecordOk>(GameServerCommand.GS_GET_GUILD_BATTLE_RECORD_REQUEST,new GsGetGuildBattleRecordRequest());Check(info.guildBattleRecord.currentBattlePoint==points&&p.Competitive.Records[29].Used==entries&&p.Guild.BattleVolunteers.Count==1,"Restart retains score, entry count, cooldown, volunteer ledger and history");}
        File.WriteAllText("I:/BnsmModernWork/Verification/guild-battle-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Guild battle: {checks.Count} checks passed");
    }
}
