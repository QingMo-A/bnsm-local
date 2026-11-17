using System.Security.Cryptography;
using System.Text.Json;

internal static class CastleRaidRegression
{
    private static readonly string Root="I:/BnsmModernWork/Verification";
    internal static void Prepare(LocalRuntime runtime)
    {
        CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();p.Competitive.HomeMap=1;p.Decks[13]=p.Deck.ToList();
        foreach(uint spot in new[]{12u,13u,22u,23u})p.Competitive.Castles["1/"+spot]=new(){Map=1,Spot=spot,Status=spot is 12 or 23?1u:0u};
        runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar))throw new ArgumentException("isolated fixture required");Directory.CreateDirectory(folder);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);Prepare(runtime);var p=runtime.Store.All().Single();
        using(var peer=new Peer(root)){
            await peer.Login(runtime,p.Account);await peer.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,new GsCreateGuildRequest{guildName="在线同门",joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17});
            uint uid=runtime.Router.Bots.All().OrderByDescending(bot=>bot.Profile.Level).First().Uid;await peer.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=uid});p.Friends.Links[uid]=new(){Joined=ProtocolRouter.Now};runtime.Store.Save();
        }
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,normalSaveUsed=false}));
        var until=DateTime.UtcNow.AddMinutes(20);
        while(DateTime.UtcNow<until){await Task.Delay(100);
        foreach(string marker in new[]{"advance-day","reopen-cleared"})if(File.Exists(Path.Combine(folder,marker))&&!File.Exists(Path.Combine(folder,marker+"-ready"))){lock(runtime.Store.Gate){
            ulong priorDay=ProtocolRouter.TowerDay(ProtocolRouter.Now)-1;
            p.Competitive.CastleRaidSummonDay=priorDay;
            foreach(var spot in p.Competitive.Castles.Values)if(spot.Raid is {} raid)raid.SummonDay=Math.Min(raid.SummonDay,priorDay);
            runtime.Store.Save();File.WriteAllText(Path.Combine(folder,marker+"-ready"),"Recorded summon dates elapsed in this isolated test save.");
        }}
        if(File.Exists(Path.Combine(folder,"prepare-final"))&&!File.Exists(Path.Combine(folder,"final-ready"))){lock(runtime.Store.Gate){
            var raid=p.Competitive.Castles["1/22"].Raid;if(raid!=null&&p.ActiveStage==null){raid.State=FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE;raid.Hp=39028;runtime.Store.Save();File.WriteAllText(Path.Combine(folder,"final-ready"),"Original final-phase HP seeded for native final-combat verification; no reward or result fabricated.");}
        }}}
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine(Root,"castle-monster-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool value,string note){if(!value)throw new InvalidDataException(note);checks.Add(note);Console.WriteLine("PASS "+note);}
        uint ownerId;ulong generation;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            Prepare(runtime);var p=runtime.Store.All().Single();ownerId=p.Uid;using var peer=new Peer(root);await peer.Login(runtime,p.Account);var catalog=new CompetitiveCatalog(root);
            await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            var count=await peer.Request<GsGetFriendRaidCountOk>(GameServerCommand.GS_GET_FRIEND_RAID_PLAY_COUNT_REQUEST,new GsGetFriendRaidCountRequest());
            Check(count.todayFriendRaidCount==(uint)runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.MAX_FRIEND_RAID_COUNT]&&count.todayFriendRaidCount>0,"Native count packet reports remaining entries, matching the original preview and deck admission");
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.Inventory,p.WorldBossPoint,p.Competitive,p.Raids,p.ActiveStage,p.ActiveBattleStamp});
            foreach(var map in catalog.CastleMaps)foreach(var row in map.SpotList.Where(s=>catalog.Castles.Any(c=>c.Id==s.CastleId&&c.BossId>0))){
                p.Competitive.HomeMap=(uint)map.MapId;p.Competitive.CastleRaidSummonDay=0;p.Competitive.Castles.Clear();var spot=new LocalCastleSpot{Map=(uint)map.MapId,Spot=(uint)row.SpotId};p.Competitive.Castles[$"{map.MapId}/{row.SpotId}"]=spot;
                var request=new GsFriendRaidBossSummonRequest{mapId=(uint)map.MapId,spotId=(uint)row.SpotId};string before=State();
                await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,request,success:false);Check(State()==before,"Summoning requires the actual linked occupied castle on map "+map.MapId+" spot "+row.SpotId);
                p.Competitive.Castles[$"{map.MapId}/{row.LinkSpot[0]}"]=new(){Map=(uint)map.MapId,Spot=(uint)row.LinkSpot[0],Status=1};
                var summoned=await peer.Request<GsFriendRaidBossSummonOk>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,request);
                Check(summoned.raidBattleAvaliable&&summoned.todaySummonCount==1&&spot.Raid?.Hp>0,"Both original monsters summon on authored map "+map.MapId+" spot "+row.SpotId);
                before=State();await peer.Request<GsFriendRaidBossSummonOk>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,request,retry:true);Check(State()==before,"Same-sequence summon retry cannot reset HP, reroll or charge twice");
                var failure=await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,request,success:false);Check(State()==before&&failure.errorCode==(uint)ServerErrorCode.ERROR_ALREADY_RAID_BOSS_EXISTS,"An already active monster rejects resummon with its specific original error");
            }
            p.Competitive.HomeMap=1;p.Competitive.CastleRaidSummonDay=0;p.Competitive.Castles.Clear();foreach(uint id in new[]{12u,13u})p.Competitive.Castles["1/"+id]=new(){Map=1,Spot=id,Status=id==12?1u:0u};
            await peer.Request<GsFriendRaidBossSummonOk>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,new GsFriendRaidBossSummonRequest{mapId=1,spotId=13});var raid=p.Competitive.Castles["1/13"].Raid!;generation=raid.Generation;
            uint boss=2631001,full=raid.Hp;var startRequest=new GsStartFriendRaidBattleRequest{castleOwnerId=p.Uid,mapId=1,spotId=13,battleType=raid.State};
            var start=await peer.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest);
            Check(start.hp==full&&start.timeStamp>generation&&p.Raids.ActiveMode==6,"Native cooperative start binds an immutable boss HP snapshot and a unique attempt token");
            string started=State();await peer.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest,retry:true);Check(State()==started,"Start retry cannot spend a second entry");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=10,mode=3,stamp=start.timeStamp});
            Check(p.ActiveStage==null&&raid.Hp==full&&!raid.Participants.Contains(p.Uid)&&raid.History.Count==0,"Retreat clears only the attempt, preserving HP and avoiding fabricated contribution or rewards");
            var later=await peer.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest);
            GsEndFriendRaidBattleRequest End(uint uid,ulong stamp,uint damage,FriendRaidBattleType phase)=>new(){castleOwnerId=p.Uid,mapId=1,spotId=13,bossID=boss,battleType=phase,timeStamp=stamp,damageAmount=damage,cardSlot1=p.Deck[0],cardSlot2=p.Deck[1],cardSlot3=p.Deck[2],cardSlot4=p.Deck[3],cardSlot5=p.Deck[4],battleHistory=new(){gameMode=(BscTable.GAME_MODE)(phase==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE?4:3)},battleEndSyncData=new()};
            string beforeEnd=State();await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BATTLE_END_REQUEST,End(p.Uid,start.timeStamp,96938,raid.State),success:false);Check(State()==beforeEnd,"A late result from the abandoned token cannot clear or reward the new attempt");
            uint essence=p.WorldBossPoint;var end=End(p.Uid,later.timeStamp,96938,raid.State);var settled=await peer.Request<GsEndFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_END_REQUEST,end);
            Check(settled.hp==full-96938&&p.WorldBossPoint==essence+200&&raid.Participants.Contains(p.Uid)&&p.ActiveStage==null,"Perfect cooperative settlement applies exact original normal plus perfect rewards and reduces real shared HP");
            beforeEnd=State();await peer.Request<GsEndFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_END_REQUEST,end,retry:true);Check(State()==beforeEnd,"Result retry cannot duplicate damage, history or rewards");
            await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest,success:false);Check(State()==beforeEnd,"One participant cannot challenge the same cooperative summon twice");
            var history=await peer.Request<GsGetFriendRaidBattleHistoryOk>(GameServerCommand.GS_GET_FRIEND_RAID_BATTLE_HISTORY_REQUEST,new GsGetFriendRaidBattleHistoryRequest{castleOwnerId=p.Uid,mapId=1,spotId=13});Check(history.historyList.Count==1&&history.historyList[0].damageAmount==96938,"Native contribution history includes only successfully settled battles");
            for(int i=0;i<4;i++){
                var q=runtime.Store.GetOrCreate("castle-helper-"+i);q.Level=99;q.CardIds=p.CardIds.ToList();q.CardStates=p.CardStates;q.Deck=p.Deck.ToList();q.Decks[13]=p.Deck.ToList();q.Friends.Links[p.Uid]=new(){Joined=ProtocolRouter.Now};
                using var helper=new Peer(root);await helper.Login(runtime,q.Account);
                var map=await helper.Request<GsGetFriendCastleMapInfoOk>(GameServerCommand.GS_GET_FRIEND_CASTLE_MAP_REQUEST,new GsGetFriendCastleMapInfoRequest{targetUid=p.Uid});Check(map.castleBattleMapSpotInfoList.Single(x=>x.spotId==13).bossHp==raid.Hp,"A friend sees the owner's authoritative cooperative HP instead of a synthetic opponent map");
                var entered=await helper.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest);
                await helper.Request<GsEndFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_END_REQUEST,End(q.Uid,entered.timeStamp,96938,FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE));
            }
            Check(raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE&&raid.Hp==24234,"Five real protocol participants transition the original shared HP to the authored final phase");
            p.Raids.Used[6]=0;startRequest.battleType=raid.State;var final=await peer.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,startRequest);essence=p.WorldBossPoint;
            var complete=await peer.Request<GsEndFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_END_REQUEST,End(p.Uid,final.timeStamp,24234,raid.State));
            Check(complete.nowState==FriendRaidBattleType.FRIEND_RAID_FINISHED&&raid.Hp==0&&p.WorldBossPoint==essence+300&&complete.earnedItemList.Count>0,"Final settlement grants the exact original essence and group-item reward, then persists finished HP");

            p.Competitive.Castles["1/23"]=new(){Map=1,Spot=23,Status=1};
            p.Competitive.Castles["1/22"]=new(){Map=1,Spot=22};
            var today=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            Check(today.castleBattleMapSpotInfoList.Single(x=>x.spotId==13).raidBossState==FriendRaidBattleType.FRIEND_RAID_FINISHED&&today.castleBattleMapSpotInfoList.Single(x=>x.spotId==22).todayBossSummonCount==1,"The cleared tile stays finished on its summon day and the other tile shows the shared quota");
            string finished=State();
            foreach(uint id in new[]{13u,22u}){
                var blockedSummon=await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,new GsFriendRaidBossSummonRequest{mapId=1,spotId=id},success:false);
                Check(blockedSummon.errorCode==(uint)ServerErrorCode.ERROR_OVER_MAX_COUNT&&State()==finished,"Same-day summon on tile "+id+" is rejected without consuming materials or resetting history");
            }
            p.Competitive.CastleRaidSummonDay--;raid.SummonDay--;
            await peer.Request<GsFriendRaidBossSummonOk>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,new GsFriendRaidBossSummonRequest{mapId=1,spotId=22});
            var assisted=p.Competitive.Castles["1/22"].Raid!;
            var residents=runtime.Router.Bots.All().OrderByDescending(bot=>bot.Profile.Level).ToArray();
            uint botId=residents[0].Uid, stranger=residents[1].Uid;
            p.Friends.Links[botId]=new(){Joined=ProtocolRouter.Now};
            var listRequest=new LocalCastleRaidHelpListRequest{mapId=1,spotId=22};
            var offer=await peer.Request<LocalCastleRaidHelpList>(GameServerCommand.GS_FRIEND_RAID_HELP_SEND_FRIENDS_LIST_REQUEST,listRequest);
            Check(offer.generation==assisted.Generation&&offer.hp==assisted.Hp&&offer.eligibleUidList.SequenceEqual(new[]{botId}),"Help list is tied to the current monster and lists only eligible resident friends");
            var help=new LocalCastleRaidHelpRequest{mapId=1,spotId=22,generation=offer.generation,targetUidVec=[botId,stranger]};
            await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help,success:false);
            Check(assisted.Hp==offer.hp&&assisted.History.Count==0,"An invalid friend in a batch cannot apply partial damage");
            help.targetUidVec=[botId];help.generation--;
            await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help,success:false);
            help.generation=offer.generation;
            var other=runtime.Store.All().First(actor=>actor.Uid!=p.Uid);using var visitor=new Peer(root);await visitor.Login(runtime,other.Account);
            await visitor.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help,success:false);
            Check(assisted.Hp==offer.hp&&raid.Hp==0,"A different owner or summon generation cannot use the invitation");
            var cooperative=new GsStartFriendRaidBattleRequest{castleOwnerId=p.Uid,mapId=1,spotId=22,battleType=assisted.State};
            var underway=await visitor.Request<GsStartFriendRaidBattleOk>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,cooperative);
            var blocked=await peer.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help,success:false);
            Check(blocked.errorCode==(uint)ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS&&assisted.Hp==offer.hp,"Invitations cannot change HP while a real player is fighting that summon");
            await visitor.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=10,mode=3,stamp=underway.timeStamp});
            assisted.Hp=1;runtime.Store.Save();
            var inventory=JsonSerializer.Serialize(p.Inventory);essence=p.WorldBossPoint;var entries=p.Raids.Used.GetValueOrDefault(6);
            await peer.Request<object>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help);
            await peer.Request<object>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help,retry:true);
            await peer.Request<object>(GameServerCommand.GS_FRIEND_RAID_HELP_REQUEST,help);
            var assistLog=await peer.Request<GsGetFriendRaidBattleHistoryOk>(GameServerCommand.GS_GET_FRIEND_RAID_BATTLE_HISTORY_REQUEST,new GsGetFriendRaidBattleHistoryRequest{castleOwnerId=p.Uid,mapId=1,spotId=22});
            var finalOffer=await peer.Request<LocalCastleRaidHelpList>(GameServerCommand.GS_FRIEND_RAID_HELP_SEND_FRIENDS_LIST_REQUEST,listRequest);
            Check(finalOffer.state==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE&&finalOffer.battleAvailable&&finalOffer.eligibleUidList.Count==0&&assisted.Participants.Contains(botId),"An assist can open the authored final battle and hides the invitation list");
            Check(assistLog.historyList.Count==1&&assistLog.historyList[0].damageAmount==1&&assistLog.historyList[0].attackUserInfo.uid==botId&&assistLog.historyList[0].attackUserInfo.nick.EndsWith("·人机")&&p.WorldBossPoint==essence&&p.Raids.Used.GetValueOrDefault(6)==entries&&JsonSerializer.Serialize(p.Inventory)==inventory,"A resident contributes once, with labeled history and no player rewards or entry cost");
            cooperative.battleType=assisted.State;
            await visitor.Request<FailPacket>(GameServerCommand.GS_FRIEND_RAID_BATTLE_START_REQUEST,cooperative,success:false);
            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.Find(ownerId)!;using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            var map=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());var info=map.castleBattleMapSpotInfoList.Single(x=>x.spotId==13);
            var cleared=p.Competitive.Castles["1/13"].Raid!;
            Check(info.raidBossState==FriendRaidBattleType.FRIEND_RAID_NONE&&info.bossHp==0&&cleared.Generation==generation&&cleared.State==FriendRaidBattleType.FRIEND_RAID_FINISHED,"An old completed tile reopens on the owner's map while its generation and final state are preserved at restart");
            var assisted=p.Competitive.Castles["1/22"].Raid!;
            Check(assisted.BotHelpers.Count==1&&assisted.History.Count==1&&assisted.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE,"Restart retains the bot participation, labeled history and final phase");
            Check(info.todayBossSummonCount==1,"Restart preserves a summon used on a different tile");
            p.Competitive.CastleRaidSummonDay--;assisted.SummonDay--;
            var again=await peer.Request<GsFriendRaidBossSummonOk>(GameServerCommand.GS_FRIEND_RAID_BOSS_SUMMON_REQUEST,new GsFriendRaidBossSummonRequest{mapId=1,spotId=13});
            var fresh=p.Competitive.Castles["1/13"].Raid!;
            Check(again.todaySummonCount==1&&fresh.Generation>generation&&fresh.Hp>0&&fresh.History.Count==0&&assisted.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE,"Next day's summon starts a fresh generation on the cleared tile without cancelling other live raids");
        }
        File.WriteAllText(Path.Combine(Root,"castle-monster-server-latest.json"),JsonSerializer.Serialize(new{passed=true,checks,normalSaveUsed=false,folder,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
