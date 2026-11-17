using System.Security.Cryptography;
using System.Text;

internal sealed partial class ProtocolRouter
{
    private GuildCatalog? guildCatalog;
    private GuildCatalog Guilds=>guildCatalog??=new(RootDirectory);
    private IEnumerable<LocalGuild> AllGuilds=>store.All().SelectMany(x=>x.FoundedGuilds);
    private LocalGuild? Guild(uint id)=>AllGuilds.FirstOrDefault(x=>x.Id==id&&!x.Disbanded);
    private LocalGuild? MyGuild(LocalPlayer p)=>Bots.Find(p.Uid)!=null?AllGuilds.FirstOrDefault(g=>!g.Disbanded&&g.Members.ContainsKey(p.Uid)):Guild(p.Guild.GuildId) is {} g&&g.Members.ContainsKey(p.Uid)?g:null;
    // Native guild UI settles attendance at 22:00 + 2 minutes, China time.
    internal static ulong GuildAttendanceDay(ulong now)=>(now+8*3600-22*3600-120)/86400;
    private uint Attended(LocalGuild g)=>(uint)g.Members.Values.Count(x=>x.AttendanceDay==g.AttendanceDay);
    private void RefreshGuilds(ulong? at=null)
    {
        ulong now=at??Now,day=GuildAttendanceDay(now),daily=TowerDay(now);bool dirty=false;
        foreach(var u in store.All().Select(p=>p.Guild).Concat(store.All().SelectMany(p=>p.GuildBotActivities.Values).Select(a=>a.User))){
            if(u.Day<daily){u.Day=daily;u.WorshipCount=0;u.Worshiped.Clear();u.BattleVolunteers.Clear();u.Slots.Clear();u.CurrentSlot=0;u.Resets=0;u.ContributionTable=0;dirty=true;}
        }
        foreach(var player in store.All())if(player.Guild.BattleDay<daily){player.Guild.BattleDay=daily;player.Guild.BattleVolunteers.Clear();if(player.Competitive.Records.TryGetValue(29,out var battle))battle.Used=battle.Bought=battle.CoolResets=0;dirty=true;}
        foreach(var g in AllGuilds.Where(x=>!x.Disbanded)){
            ulong battleWeek=(daily+3)/7;if(g.Battle.Week<battleWeek){g.Battle.Week=battleWeek;g.Battle.Battles=g.Battle.Wins=g.Battle.Points=0;dirty=true;}
            if(g.AttendanceDay<day){
                uint count=Attended(g);
                var reward=Guilds.Attendance.Where(x=>x.Level==g.Level&&x.AttendanceNumber<=count).OrderByDescending(x=>x.AttendanceNumber).FirstOrDefault();
                if(reward!=null)foreach(var pair in g.Members.Where(x=>x.Value.AttendanceDay==g.AttendanceDay)){
                    var p=store.Find(pair.Key);if(p!=null&&p.Guild.AttendanceRewards.Count<32)p.Guild.AttendanceRewards.Add(new(){GuildId=g.Id,DataId=(uint)reward.DataId});
                    else if(Bots.Find(pair.Key)!=null){var activity=EnsureBotGuildActivity(g,pair.Key);if(activity.Rewards.Count<32&&!activity.Rewards.Any(r=>r.GuildId==g.Id&&r.Day==g.AttendanceDay))activity.Rewards.Add(new(){GuildId=g.Id,DataId=(uint)reward.DataId,Day=g.AttendanceDay});}
                }
                g.YesterdayAttendance=g.AttendanceDay+1==day?count:0;g.AttendanceDay=day;dirty=true;
            }
            ulong week=(daily+3)/7;
            foreach(var m in g.Members.Values)if(m.Week<week){m.Week=week;m.WeeklyContribution=0;dirty=true;}
        }
        if(dirty)store.Save();
    }
    private PKGuildInfo GuildInfo(LocalGuild g)=>new(){guildID=g.Id,guildName=g.Name,guildGreeting=g.Greeting,guildMasterName=GuildActor(g.Master)?.Nickname??"",emblemID=g.Emblem,borderID=g.Border,joinType=g.JoinType,joinMinLevel=g.MinLevel,guildLevel=g.Level,ranking=GuildBattleRank(g),guildPoint=g.Points,registTime=g.Created,userCount=(uint)g.Members.Count};
    private GsGetMyGuildInfoOk MyGuildInfo(LocalPlayer p)
    {
        var g=MyGuild(p);var waiting=AllGuilds.FirstOrDefault(x=>!x.Disbanded&&x.Applicants.Contains(p.Uid));
        var result=new GsGetMyGuildInfoOk{status=g!=null?GuildJoinStatus.GUILD_JOIN_STATUS_COMPLETE:waiting!=null?GuildJoinStatus.GUILD_JOIN_STATUS_WAITING:GuildJoinStatus.GUILD_JOIN_STATUS_NONE,grade=g?.Members[p.Uid].Grade??GuildUserGrade.GUILD_USER_GRADE_NONE,guildInfo=g==null?new(){guildLevel=1}:GuildInfo(g),withdrawTime=p.Guild.WithdrawTime,latestGuildFlag=p.Guild.Latest,guildInvitedListCount=(uint)AllGuilds.Count(x=>!x.Disbanded&&x.Invitees.Contains(p.Uid)),worshipCount=p.Guild.WorshipCount,worshipedFlag=p.Guild.WorshipGold>0,rvrBattleOpen=false};
        result.worshipedUidList.AddRange(p.Guild.Worshiped);
        if(g!=null){result.attendanceFlag=g.Members[p.Uid].AttendanceDay==g.AttendanceDay;result.attendanceRewardID=(int)(p.Guild.AttendanceRewards.FirstOrDefault(x=>x.GuildId==g.Id)?.DataId??0);result.yesterdayAttendanceCount=g.YesterdayAttendance;result.todayAttendanceCount=Attended(g);result.hasCompleteMission=GuildMissions(g).Any(x=>x.status==MissionStatus.MISSION_STATUS_COMPLETED);}
        return result;
    }
    private PKThumbnailUserData GuildThumbnail(LocalPlayer p)
    {
        var n=NameCard(p);bool resident=Bots.Find(p.Uid)!=null;string nick=!resident?p.Nickname:p.Nickname[..Math.Min(9,p.Nickname.Length)]+"·人机";
        // Residents are available while this local server is running. Their
        // immutable build profiles have no player login heartbeat (often zero).
        // Project presence into the packet, without rewriting resident saves.
        return new(){uid=p.Uid,nick=nick,level=p.Level,account=p.Account,vipGrade=n.commonInfo.vipGrade,titleId=n.commonInfo.titleId,avatarCardId=n.avatarCardId,leaderCardInfo=n.leaderCardInfo,logoutTime=resident?Now:p.LastActivity};
    }
    private void GuildLog(LocalGuild g,LocalPlayer p,GuildHistoryLogType type,uint data=0,string target="",uint extra=0)
    {
        g.History.Add(new(){Type=type,Data=data,Extra=extra,Time=Now,Name=p.Nickname,Target=target});if(g.History.Count>200)g.History.RemoveAt(0);
    }
    private void GuildJoin(LocalGuild g,LocalPlayer p)
    {
        foreach(var other in AllGuilds){other.Applicants.Remove(p.Uid);other.Invitees.Remove(p.Uid);}
        bool bot=Bots.Find(p.Uid)!=null;g.Members[p.Uid]=new(){Joined=Now,Week=(TowerDay(Now)+3)/7,IsBot=bot};
        if(bot)ScheduleBotGuildActivity(g,p,Now);
        var account=GuildAccount(p);account.GuildId=g.Id;account.Latest=LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_APPROVE_JOIN_GUILD;
        GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_JOIN);
    }
    private bool GuildNameValid(string name)=>name.Length is >=2 and <=8&&name.All(c=>char.IsLetterOrDigit(c)||char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.OtherLetter);
    private bool GuildSettings(uint emblem,uint border,GuildJoinType join,uint min)=>Guilds.Emblems.Any(x=>x.Id==emblem)&&border==0&&Enum.IsDefined(join)&&min>=catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_GUILD)&&min<=99;
    private bool GuildReward(LocalPlayer p,ShopGrant grant,List<BscTable.REWARD_TYPE> types,List<int> ids,List<int> counts)
    {
        if(types.Count!=ids.Count||ids.Count!=counts.Count)return false;
        for(int i=0;i<types.Count;i++)if(!AddShopReward(p,grant,types[i],ids[i],counts[i]))return false;
        return CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0);
    }
    private IEnumerable<PKProgressingMissionData> GuildMissions(LocalGuild g)
    {
        foreach(var row in Guilds.Missions){
            if(g.ClaimedMissions.Contains((uint)row.MissionId))continue;bool open=true;
            for(int i=0;i<row.OpenConditionType.Count;i++){
                int value=row.OpenConditionValue[i];open&=(int)row.OpenConditionType[i] switch{7=>g.Level>=value,4=>g.ClaimedMissions.Contains((uint)value),_=>false};
            }
            if(!open)continue;
            uint count=row.CheckType==BscTable.CHECK_TYPE.MCT_GUILD_CONTRIBUTION?g.DonationCount:0;
            yield return new(){missionId=(uint)row.MissionId,count=count,status=count>=row.NeedCount?MissionStatus.MISSION_STATUS_COMPLETED:MissionStatus.MISSION_STATUS_PROGRESSING,registTime=g.Created};
        }
    }
    private void GuildContributionSlots(LocalPlayer p)
    {
        var u=p.Guild;if(u.Slots.Count!=0)return;
        double sample=RandomNumberGenerator.GetInt32(1000000)/1000000d*Guilds.Contributions.Sum(x=>x.Prob);var table=Guilds.Contributions.Last();
        foreach(var candidate in Guilds.Contributions){sample-=candidate.Prob;if(sample<0){table=candidate;break;}}
        u.ContributionTable=(uint)table.Id;
        foreach(var group in table.ContributionSlotInfoList.Where(x=>x.PcLvMin<=p.Level&&p.Level<=x.PcLvMax).GroupBy(x=>x.SlotId).OrderBy(x=>x.Key)){
            sample=RandomNumberGenerator.GetInt32(1000000)/1000000d*group.Sum(x=>x.SlotProb);var chosen=group.Last();foreach(var row in group){sample-=row.SlotProb;if(sample<0){chosen=row;break;}}u.Slots.Add((uint)chosen.SlotSeq);
        }
    }
    private GsGetGuildContributionInfoOk GuildContributionInfo(LocalPlayer p)
    {
        GuildContributionSlots(p);var u=p.Guild;var result=new GsGetGuildContributionInfoOk{guildContributionDataId=u.ContributionTable,currentSlotId=u.CurrentSlot,resetCount=u.Resets};result.slotSeqList.AddRange(u.Slots);return result;
    }
    private PKChatMessage[] GuildMessages(LocalGuild g,uint after,uint before=0)
    {
        var rows=g.Chat.Select(x=>(PKChatMessage)WireCodec.Deserialize(typeof(PKChatMessage),Convert.FromBase64String(x))!).Where(x=>before>0?x.chatSeq<before:x.chatSeq>after).TakeLast(30).ToList();
        while(rows.Sum(x=>WireCodec.Serialize(x).Length+4)>7000)rows.RemoveAt(0);return rows.ToArray();
    }

    private Reply? HandleGuild(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!name.Contains("GUILD",StringComparison.Ordinal)&&request is not GsGetVolunteerCardDetailInfoRequest)return null;
        // Legacy roster/invitation packets are included, as their native socket
        // coroutines wait until dataCount == totalDataCount.
        bool read=name is "GS_GET_MY_GUILD_INFO_REQUEST" or "GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST" or "GS_GET_RECOMMEND_GUILD_LIST_REQUEST" or "GS_SEARCH_GUILD_INFO_BY_NAME_REQUEST" or "GS_GET_GUILD_INFO_REQUEST" or "GS_GET_GUILD_JOIN_WAITING_LIST_REQUEST" or "GS_GET_GUILD_INVITED_LIST_REQUEST" or "GS_GET_GUILD_HISTORY_REQUEST" or "GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST" or "GS_GET_GUILD_WORSHIPED_INFO_REQUEST" or "GS_GET_GUILD_MISSION_LIST_REQUEST" or "GS_FETCH_GUILD_CHAT_MESSAGE_REQUEST" or "GS_GET_GUILD_BATTLE_RECORD_REQUEST" or "GS_SEARCH_GUILD_BATTLE_ENEMY_LIST_REQUEST" or "GS_GET_GUILD_BATTLE_USED_VOLUNTEER_LIST_REQUEST" or "GS_GET_GUILD_BATTLE_ENEMY_DECK_INFO_REQUEST" or "GS_GET_GUILD_BATTLE_VOLUNTEER_CARD_BATTLE_INFO_REQUEST" or "GS_GET_GUILD_ARENA_BATTLE_HISTORY_REQUEST" or "GS_GET_RAINKING_BOARD_GUILD_DETAIL_REQUEST" or "GS_GET_GUILD_RVR_BATTLE_LOBBYINFO_REQUEST" or "GS_GET_GUILD_RVR_BATTLE_RANK_INFO_REQUEST";
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}"))),fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(!read&&p.GuildReceipts.TryGetValue(key,out var prior))return prior.Fingerprint==fingerprint?new(prior.Command,prior.TypeName==""?null:WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name):fail("guild sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var reply=GuildOperation(name,request,p,ok,fail);
        if(reply?.Name.EndsWith("_OK")==true){
            if(!read){if(p.GuildReceipts.Count>=256)p.GuildReceipts.Remove(p.GuildReceipts.Keys.First());p.GuildReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message?.GetType().FullName??"",Payload=reply.Message==null?"":Convert.ToBase64String(WireCodec.Serialize(reply.Message))};}
            store.Save();
        }return reply;
    }
    private Reply? GuildOperation(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_GUILD_INFO)=>fail(why,code);
        var g=MyGuild(p);var u=p.Guild;
        if(name=="GS_GET_MY_GUILD_INFO_REQUEST")return ok(MyGuildInfo(p));
        if(name=="GS_GET_RECOMMEND_GUILD_LIST_REQUEST"||request is GsSearchGuildInfoByNameRquest){
            string search=request is GsSearchGuildInfoByNameRquest s?s.guildName.Trim():"";
            var list=AllGuilds.Where(x=>!x.Disbanded&&(search!=""?x.Name.Contains(search,StringComparison.OrdinalIgnoreCase):x.JoinType!=GuildJoinType.GUILD_JOIN_TYPE_UNABLE)).OrderBy(x=>x.Id).Take(50).Select(GuildInfo).ToList();
            if(request is GsSearchGuildInfoByNameRquest){var r=new GsSearchGuildInfoByNameOk{resultCount=(uint)list.Count};r.guildInfoList.AddRange(list);return ok(r);}
            var rec=new GsGetRecommendGuildListOk{resultCount=(uint)list.Count};rec.guildInfoList.AddRange(list);return ok(rec);
        }
        if(request is GsGetGuildInfoRequest get){var target=Guild(get.guildID);if(target==null&&MyGuild(p)!=null&&CompetitiveOffers(p,29).FirstOrDefault(x=>x.Uid==get.guildID) is {} offer){var npc=GuildBattleEnemy(p,offer);return ok(new GsGetGuildInfoOK{guildInfo=new(){guildID=npc.enemyGuildId,guildName=npc.enemyGuildNick,guildLevel=npc.enemyGuildLevel,emblemID=npc.enemyGuildEmblemID,ranking=npc.enemyGuildRank,userCount=5}});}return target==null?Bad("guild not found"):ok(new GsGetGuildInfoOK{guildInfo=GuildInfo(target)});}
        if(request is GsGetRankingBoardGuildDetailInfoRequest detail){var target=Guild(detail.targetGuildId);return target==null?Bad("guild not found"):ok(new GsGetRankingBoardGuildDetailInfoOk{targetGuildId=target.Id,guildInfo=GuildInfo(target)});}
        if(name=="GS_GET_GUILD_INVITED_LIST_REQUEST"){
            var r=new GsGetGuildInvitedListOk();r.guildInfoList.AddRange(AllGuilds.Where(x=>!x.Disbanded&&x.Invitees.Contains(p.Uid)).Select(GuildInfo));r.dataCount=r.totalDataCount=(uint)r.guildInfoList.Count;return ok(r);
        }
        if(request is GsCreateGuildRequest create){
            if(g!=null)return Bad("already joined a guild",ServerErrorCode.ERROR_ALREADY_GUILD_JOIN);
            if(p.ActiveStage!=null)return Bad("finish the current battle first");
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_GUILD))return Bad("guild opens at the original player level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(!GuildNameValid(create.guildName))return Bad("guild name must be 2..8 letters or numbers",ServerErrorCode.ERROR_INVALID_GUILD_NAME);
            if(AllGuilds.Any(x=>!x.Disbanded&&x.Name.Equals(create.guildName,StringComparison.OrdinalIgnoreCase)))return Bad("guild name already exists",ServerErrorCode.ERROR_EXIST_GUILD_NAME);
            if(!GuildSettings(create.emblemID,create.borderID,create.joinType,create.joinMinLevel)||create.guildGreeting.Length>100||create.guildGreeting.Any(char.IsControl))return Bad("invalid guild settings");
            var fee=catalog.MoneyResources.Single(x=>x.MoneyEventType==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_CREATE_GUILD);
            if(fee.Value<0||!CanPayAndGrant(p,new(),fee.MoneyType,(uint)fee.Value))return Bad("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            var made=new LocalGuild{Battle=new(){Week=(TowerDay(Now)+3)/7},TotalDonationPoints=0,Id=AllGuilds.Select(x=>x.Id).DefaultIfEmpty(0u).Max()+1,Name=create.guildName,Greeting=create.guildGreeting,Emblem=create.emblemID,Border=create.borderID,JoinType=create.joinType,MinLevel=create.joinMinLevel,Created=Now,Master=p.Uid,AttendanceDay=GuildAttendanceDay(Now)};
            ApplyShopGrant(p,new(),fee.MoneyType,(uint)fee.Value,[],[]);p.FoundedGuilds.Add(made);GuildJoin(made,p);made.Members[p.Uid].Grade=GuildUserGrade.GUILD_USER_GRADE_MASTER;u.Latest=LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_NONE;made.History.Clear();GuildLog(made,p,GuildHistoryLogType.GUILD_HISTORY_CREATE);
            return ok(new GsCreateGuildOk{currentCoinInfo=Coins(p),status=GuildJoinStatus.GUILD_JOIN_STATUS_COMPLETE,grade=GuildUserGrade.GUILD_USER_GRADE_MASTER,guildInfo=GuildInfo(made)});
        }
        if(request is GsJoinGuildRequest||request is GsAcceptGuildInviteRequest){
            uint id=request is GsJoinGuildRequest join?join.guildID:((GsAcceptGuildInviteRequest)request).acceptGuildId;var target=Guild(id);
            if(g!=null)return Bad("already joined a guild",ServerErrorCode.ERROR_ALREADY_GUILD_JOIN);
            if(target==null||target.JoinType==GuildJoinType.GUILD_JOIN_TYPE_UNABLE)return Bad("guild is not accepting members");
            if(p.ActiveStage!=null||p.Level<target.MinLevel)return Bad("original join level not reached or battle active",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(target.Members.Count>=Guilds.Levels.Single(x=>x.GuildLevel==target.Level).MaxUserCount)return Bad("guild is full",ServerErrorCode.ERROR_OVER_MAX_GUILD_USER_COUNT);
            bool invited=request is GsAcceptGuildInviteRequest;if(invited&&!target.Invitees.Contains(p.Uid))return Bad("invitation no longer exists",ServerErrorCode.ERROR_INVALID_GUILD_APPROVE_INFO);
            bool complete=invited||target.JoinType==GuildJoinType.GUILD_JOIN_TYPE_OPEN;
            if(complete)GuildJoin(target,p);else{foreach(var old in AllGuilds)old.Applicants.Remove(p.Uid);target.Applicants.Add(p.Uid);}
            return invited?ok(new GsAcceptGuildInviteOk{guildInfo=GuildInfo(target)}):ok(new GsJoinGuildOk{guildID=id,status=complete?GuildJoinStatus.GUILD_JOIN_STATUS_COMPLETE:GuildJoinStatus.GUILD_JOIN_STATUS_WAITING,grade=complete?GuildUserGrade.GUILD_USER_GRADE_NORMAL:GuildUserGrade.GUILD_USER_GRADE_NONE,guildInfo=GuildInfo(target)});
        }
        if(request is GsDenyGuildInviteRequest deny){Guild(deny.denyGuildId)?.Invitees.Remove(p.Uid);return ok(null);}
        if(name=="GS_CONFIRM_LATEST_GUILD_STATUS_REQUEST"){u.Latest=LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_NONE;return ok(null);}
        if(g==null)return Bad("not a guild member",ServerErrorCode.ERROR_NOT_GUILD_USER);
        bool master=g.Master==p.Uid,admin=master||g.Members[p.Uid].Grade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER;
        if(name=="GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST"){
            var result=new GsGetMyGuildUserInfoListOk();foreach(var pair in g.Members){var member=GuildActor(pair.Key);if(member==null)continue;result.guildUserInfoList.Add(new(){grade=pair.Value.Grade,registTime=pair.Value.Joined,ContributionPoint=pair.Value.Contribution,weeklyContributionPoint=pair.Value.WeeklyContribution,guildUserData=GuildThumbnail(member),attendanceFlag=pair.Value.AttendanceDay==g.AttendanceDay});}result.totalDataCount=result.dataCount=(uint)result.guildUserInfoList.Count;return ok(result);
        }
        if(name=="GS_GET_GUILD_JOIN_WAITING_LIST_REQUEST"){
            if(!admin)return Bad("guild administrator required",ServerErrorCode.ERROR_GUILD_USER_GRADE);var r=new GsGetGuildJoinWaitingListOk{guildID=g.Id};r.userDataList.AddRange(g.Applicants.Take(20).Select(GuildActor).Where(x=>x!=null).Select(x=>GuildThumbnail(x!)));return ok(r);
        }
        if(request is GsDecisionJoinGuildRequest decision){
            if(!admin)return Bad("guild administrator required",ServerErrorCode.ERROR_GUILD_USER_GRADE);var target=GuildActor(decision.targetUid);
            if(target==null||!g.Applicants.Contains(target.Uid)||decision.decision>1)return Bad("invalid application",ServerErrorCode.ERROR_INVALID_GUILD_APPROVE_INFO);
            if(decision.decision==1){if(MyGuild(target)!=null||target.Level<g.MinLevel||g.Members.Count>=Guilds.Levels.Single(x=>x.GuildLevel==g.Level).MaxUserCount)return Bad("application no longer eligible");GuildJoin(g,target);}else g.Applicants.Remove(target.Uid);return ok(null);
        }
        if(request is GsInviteGuildRequest invite){
            if(!admin)return Bad("guild administrator required",ServerErrorCode.ERROR_GUILD_USER_GRADE);var target=GuildActor(invite.inviteUid);
            if(target==null||MyGuild(target)!=null||target.Level<g.MinLevel||target.Uid==p.Uid)return Bad("target is not an eligible local profile");
            if(g.Members.Count>=Guilds.Levels.Single(x=>x.GuildLevel==g.Level).MaxUserCount)return Bad("guild is full",ServerErrorCode.ERROR_OVER_MAX_GUILD_USER_COUNT);
            if(Bots.Find(target.Uid)!=null){if(g.JoinType==GuildJoinType.GUILD_JOIN_TYPE_UNABLE)return Bad("guild is not accepting members");GuildJoin(g,target);}else g.Invitees.Add(target.Uid);return ok(null);
        }
        if(name=="GS_WITHDRAW_GUILD_REQUEST"){
            if(master)return Bad("transfer leadership or disband before leaving",ServerErrorCode.ERROR_GUILD_USER_GRADE);GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_WITHDRAW);g.Members.Remove(p.Uid);u.GuildId=0;u.WithdrawTime=Now;return ok(null);
        }
        if(request is GsBanGuildUserRequest ban){
            if(!admin||ban.targetUid==p.Uid||!g.Members.TryGetValue(ban.targetUid,out var member)||member.Grade==GuildUserGrade.GUILD_USER_GRADE_MASTER||!master&&member.Grade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER)return Bad("not permitted to remove this member",ServerErrorCode.ERROR_GUILD_USER_GRADE);
            var target=GuildActor(ban.targetUid)!;g.Members.Remove(target.Uid);var account=GuildAccount(target);account.GuildId=0;account.Latest=LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_BAN;account.WithdrawTime=Now;GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_BAN_USER,target:target.Nickname);return ok(null);
        }
        if(request is GsUpdateGuildUserGradeRequest grade){
            if(!master||grade.targetUid==p.Uid||!g.Members.TryGetValue(grade.targetUid,out var member)||grade.changeGrade is GuildUserGrade.GUILD_USER_GRADE_NONE||!Enum.IsDefined(grade.changeGrade))return Bad("leader required and target must be another member",ServerErrorCode.ERROR_GUILD_USER_GRADE);
            if(grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER&&member.Grade!=grade.changeGrade&&g.Members.Values.Count(x=>x.Grade==grade.changeGrade)>=Guilds.Levels.Single(x=>x.GuildLevel==g.Level).MaxCommanderUserCount)return Bad("deputy limit reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var target=GuildActor(grade.targetUid)!;if(grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_MASTER&&Bots.Find(target.Uid)!=null)return Bad("transfer guild leadership to a human member",ServerErrorCode.ERROR_GUILD_USER_GRADE);var type=grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_MASTER?GuildHistoryLogType.GUILD_HISTORY_CHANGE_MASTER:grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER?GuildHistoryLogType.GUILD_HISTORY_CHANGE_GRADE_UP:GuildHistoryLogType.GUILD_HISTORY_CHANGE_GRADE_DOWN;
            if(grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_MASTER){g.Members[p.Uid].Grade=GuildUserGrade.GUILD_USER_GRADE_NORMAL;g.Master=target.Uid;}
            member.Grade=grade.changeGrade;GuildAccount(target).Latest=grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_MASTER?LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_MASTER:grade.changeGrade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER?LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_SUB_MASTER:LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_SUB_MASTER_DOWN;GuildLog(g,p,type,target:target.Nickname);return ok(null);
        }
        if(name=="GS_DISBAND_GUILD_REQUEST"){
            if(!master)return Bad("leader required",ServerErrorCode.ERROR_GUILD_USER_GRADE);if(g.Members.Count>1)return Bad("remove other members before disbanding",ServerErrorCode.ERROR_INVALID_DISBAND_GUILD_STATUS);
            g.Disbanded=true;g.Members.Clear();g.Applicants.Clear();g.Invitees.Clear();u.GuildId=0;u.WithdrawTime=Now;return ok(new GsDisbandGuildOk{currentCoinInfo=Coins(p)});
        }
        if(request is GsUpdateGuildGreetingRequest greeting){
            if(!admin||greeting.guildID!=g.Id)return Bad("guild administrator required",ServerErrorCode.ERROR_GUILD_USER_GRADE);
            if(greeting.guildGreeting.Length>100||greeting.guildGreeting.Any(c=>char.IsControl(c)&&c!='\n'))return Bad("invalid notice",ServerErrorCode.ERROR_INVALID_GUILD_GREETING_LENGTH);g.Greeting=greeting.guildGreeting;return ok(null);
        }
        if(request is GsChangeGuildInfoRequest change){
            if(!master)return Bad("leader required",ServerErrorCode.ERROR_GUILD_USER_GRADE);
            if(!GuildSettings(change.emblemID,change.borderID,change.joinType,change.joinMinLevel))return Bad("invalid guild settings");
            if(change.changeGuildNameFlag){
                if(!GuildNameValid(change.guildName))return Bad("invalid guild name",ServerErrorCode.ERROR_INVALID_GUILD_NAME);
                if(AllGuilds.Any(x=>!x.Disbanded&&x.Id!=g.Id&&x.Name.Equals(change.guildName,StringComparison.OrdinalIgnoreCase)))return Bad("guild name already exists",ServerErrorCode.ERROR_EXIST_GUILD_NAME);
                var fee=catalog.MoneyResources.Single(x=>x.MoneyEventType==BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_CHANGE_GUILD_NAME);if(!CanPayAndGrant(p,new(),fee.MoneyType,(uint)fee.Value))return Bad("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);ApplyShopGrant(p,new(),fee.MoneyType,(uint)fee.Value,[],[]);g.Name=change.guildName;
            }
            g.Emblem=change.emblemID;g.Border=change.borderID;g.JoinType=change.joinType;g.MinLevel=change.joinMinLevel;return ok(new GsChangeGuildInfoOk{guildInfo=GuildInfo(g),currentCoinInfo=Coins(p)});
        }
        if(name=="GS_GET_GUILD_HISTORY_REQUEST"){
            var h=new GsGetGuildHistoryOk{totalCount=(uint)Math.Min(50,g.History.Count)};h.historyList.AddRange(g.History.AsEnumerable().Reverse().Take(50).Select(x=>new PKGuildHistoryInfo{logType=x.Type,historyData=x.Data,historyExtraData=x.Extra,registTime=x.Time,nickName=x.Name,targetNickName=x.Target}));return ok(h);
        }
        if(name=="GS_CHECK_GUILD_ATTENDANCE_REQUEST"){
            if(u.AttendanceDay==g.AttendanceDay)return Bad("already checked in during this attendance day",ServerErrorCode.ERROR_OVER_MAX_COUNT);u.AttendanceDay=g.AttendanceDay;g.Members[p.Uid].AttendanceDay=g.AttendanceDay;return ok(new GsCheckGuildAttendanceOk{yesterdayAttendanceCount=g.YesterdayAttendance,todayAttendanceCount=Attended(g)});
        }
        if(name=="GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST"){
            var pending=u.AttendanceRewards.FirstOrDefault(x=>x.GuildId==g.Id);var reward=Guilds.Attendance.SingleOrDefault(x=>x.DataId==pending?.DataId);if(reward==null)return Bad("no settled attendance reward",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var grant=new ShopGrant();if(!GuildReward(p,grant,reward.RewardIdType,reward.RewardId,reward.RewardQuantity)||(ulong)g.Points+(uint)reward.RewardGuildPoint>uint.MaxValue)return Bad("attendance reward capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var r=new GsRecvGuildAttendanceRewardOk{rewardDataId=(uint)reward.DataId,rewardCoinInfo=GrantCoins(grant),rewardGuildPoint=(uint)reward.RewardGuildPoint};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,r.rewardItemList,[]);g.Points+=r.rewardGuildPoint;u.AttendanceRewards.Remove(pending!);r.coinInfo=Coins(p);GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_ATTENDACE_REWARD,(uint)reward.DataId);return ok(r);
        }
        if(name=="GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST")return ok(GuildContributionInfo(p));
        if(request is GsGuildContributionRequest contribution){
            GuildContributionSlots(p);var data=Guilds.Contributions.Single(x=>x.Id==u.ContributionTable);var slot=u.CurrentSlot<u.Slots.Count?data.ContributionSlotInfoList.Single(x=>x.SlotSeq==u.Slots[(int)u.CurrentSlot]):null;
            var item=contribution.itemInfo==null?null:catalog.Items.SingleOrDefault(x=>x.Id==contribution.itemInfo.itemID);
            if(slot==null||item==null||contribution.itemInfo!.itemCount!=1||p.Level<slot.PcLvMin||p.Level>slot.PcLvMax)return Bad("invalid donation slot or item");
            bool ticket=item.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_CONTRIBUTION;
            if(!ticket&&(item.Grade<slot.RequireItemGrade||!slot.RequireItemSubType.Contains(item.ItemSubType)))return Bad("item does not satisfy original donation requirement");
            LocalOwnedEquipment? equipment=null;
            if(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT){equipment=p.EquipmentItems.SingleOrDefault(x=>x.Sequence==contribution.itemInfo.itemSeq&&x.ItemId==item.Id);if(equipment==null||equipment.Locked||EquipmentOwner(p,equipment.Sequence)!=0)return Bad("equipment is missing, locked or equipped");}
            else if(p.Inventory.GetValueOrDefault((uint)item.Id)<1)return Bad("donation material not owned",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
            uint points=(uint)slot.RewardGuildPoint;
            // Local replacement for guild-war income; the original donation and
            // guild progression rewards remain unchanged.
            var grant=new ShopGrant{Tokens=points};var member=g.Members[p.Uid];
            if(!GuildReward(p,grant,slot.RewardIdType,slot.RewardId,slot.RewardQuantity)||(ulong)g.Points+points>uint.MaxValue||(ulong)member.Contribution+points>uint.MaxValue||(ulong)member.WeeklyContribution+points>uint.MaxValue)return Bad("donation reward capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsGuildContributionOk{earnedCoinInfo=GrantCoins(grant),deltaItem=contribution.itemInfo};result.deltaItem.itemCount=-1;
            if(equipment!=null)p.EquipmentItems.Remove(equipment);else p.Inventory[(uint)item.Id]--;
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.rewardItemList,[]);result.coinInfo=Coins(p);g.TotalDonationPoints=GuildDonations(g)+points;g.Points+=points;g.DonationCount++;member.Contribution+=points;member.WeeklyContribution+=points;u.CurrentSlot++;GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_GUILD_CONTRIBUTION,points);return ok(result);
        }
        if(name=="GS_GUILD_CONTRIBUTION_RESET_REQUEST"){
            GuildContributionSlots(p);
            if(u.Resets>=catalog.Numbers[BscTable.NUMBER_TYPE.GUILD_CONTRIBUTION_MAX_RESET_COUNT])return Bad("daily reset limit reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var fee=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_CONTRIBUTION_COUNT,(int)u.Resets+1,true);
            if(fee==null)return Bad("original donation reset row missing");
            var currency=fee.MoneyType==BscTable.MONEY_TYPE.MONEY_TYPE_NONE?BscTable.MONEY_TYPE.MONEY_TYPE_GOLD:fee.MoneyType;
            uint cost=fee.MoneyType==BscTable.MONEY_TYPE.MONEY_TYPE_NONE?0:(uint)fee.Value;
            if(!CanPayAndGrant(p,new(),currency,cost))return Bad("not enough refresh currency",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            ApplyShopGrant(p,new(),currency,cost,[],[]);u.Resets++;u.CurrentSlot=0;u.Slots.Clear();var info=GuildContributionInfo(p);var r=new GsGuildContributionResetOk{guildContributionDataId=info.guildContributionDataId,currentSlotId=0,resetCount=u.Resets,coinInfo=Coins(p)};r.slotSeqList.AddRange(info.slotSeqList);return ok(r);
        }
        if(name=="GS_GET_GUILD_MISSION_LIST_REQUEST"){var r=new GsGetGuildMissionListOk();r.list.AddRange(GuildMissions(g));return ok(r);}
        if(request is GsGetGuildMissionRewardRequest mission){
            if(!master)return Bad("leader required",ServerErrorCode.ERROR_GUILD_USER_GRADE);var entry=GuildMissions(g).SingleOrDefault(x=>x.missionId==mission.missionId);var row=Guilds.Missions.SingleOrDefault(x=>x.MissionId==mission.missionId);
            if(entry?.status!=MissionStatus.MISSION_STATUS_COMPLETED||row==null||(ulong)g.Points+(uint)row.RewardGuildPoint>uint.MaxValue)return Bad("mission is locked, incomplete or already claimed");g.Points+=(uint)row.RewardGuildPoint;g.ClaimedMissions.Add(mission.missionId);return ok(new GsGetGuildMissionRewardOk{missionId=mission.missionId,guildInfo=GuildInfo(g)});
        }
        if(name=="GS_GUILD_LEVEL_UP_REQUEST"){
            if(!master)return Bad("leader required",ServerErrorCode.ERROR_GUILD_USER_GRADE);var next=Guilds.Levels.SingleOrDefault(x=>x.GuildLevel==g.Level+1);if(next==null)return Bad("guild is already maximum level",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(g.Points<next.LevelUpPoint)return Bad("not enough guild points",ServerErrorCode.ERROR_NOT_ENOUGH_GUILD_POINT);if(g.Members.Count<next.LevelUpMinUserCount)return Bad("not enough members",ServerErrorCode.ERROR_NOT_ENOUGH_GUILD_USER_COUNT);
            if(p.CashPoint<next.LevelUpNeedCash)return Bad("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);g.Points-=(uint)next.LevelUpPoint;p.CashPoint-=(uint)next.LevelUpNeedCash;g.Level++;GuildLog(g,p,GuildHistoryLogType.GUILD_HISTORY_GUILD_LEVEL_UP,g.Level);return ok(new GsGuildLevelUpOk{currentCoinInfo=Coins(p),guildInfo=GuildInfo(g)});
        }
        if(request is GsGuildWorshipRequest worship){
            var target=GuildActor(worship.targetUid);var row=Guilds.Worship.SingleOrDefault(x=>x.GuildWorshipType==worship.worshipType);
            if(target==null||target.Uid==p.Uid||!g.Members.ContainsKey(target.Uid))return Bad("target is not another guild member",ServerErrorCode.ERROR_NOT_GUILD_USER);
            if(target.Level<=p.Level)return Bad("only a higher-level member can be revered",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(u.Worshiped.Contains(target.Uid))return Bad("member already revered today",ServerErrorCode.ERROR_ALREADY_WORSHIP_UID);
            if(u.WorshipCount>=catalog.VipFor(p.VipExp).WorshipAvailableCount)return Bad("daily worship limit reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(row==null||(ulong)GuildAccount(target).WorshipGold+(uint)row.WorshipedBenefitGold>uint.MaxValue)return Bad("invalid worship type");
            var grant=new ShopGrant{Stamina=(uint)row.RewardStamina};var currency=row.CostValue==0?BscTable.MONEY_TYPE.MONEY_TYPE_GOLD:row.MoneyType;
            if(!CanPayAndGrant(p,grant,currency,(uint)row.CostValue))return Bad("not enough gold or stamina capacity",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            ApplyShopGrant(p,grant,currency,(uint)row.CostValue,[],[]);u.WorshipCount++;u.Worshiped.Add(target.Uid);GuildAccount(target).WorshipGold+=(uint)row.WorshipedBenefitGold;return ok(new GsGuildWorshipOk{stamina=p.Stamina,staminaStamp=p.StaminaStamp,currentCoinInfo=Coins(p)});
        }
        if(name=="GS_GET_GUILD_WORSHIPED_INFO_REQUEST")return ok(new GsGetGuildWorshipedInfoOk{worshipedGold=u.WorshipGold});
        if(name=="GS_RECEIVE_GUILD_WORSHIPED_REWARD_REQUEST"){
            if(u.WorshipGold==0||(ulong)p.Gold+u.WorshipGold>uint.MaxValue)return Bad("no worship income or gold capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);p.Gold+=u.WorshipGold;uint earned=u.WorshipGold;u.WorshipGold=0;return ok(new GsReceiveGuildWorshipedRewardOk{worshipedGold=earned,currentCoinInfo=Coins(p)});
        }
        if(request is GsFetchGuildChatMessageRequest fetch){var r=new GsFetchGuildChatMessageOk();r.msgList.AddRange(GuildMessages(g,fetch.lastMsgSeq,fetch.beforeMsgSeq));return ok(r);}
        if(request is GsSendGuildChatMessageRequest chat){
            string message=chat.msg.Trim();if(message.Length is <1 or >200||message.Any(char.IsControl)||chat.replayMessage||chat.chatType!=GuildChatType.GUILD_CHAT_TYPE_NORMAL)return Bad("invalid local guild chat message");
            var n=NameCard(p);var msg=new PKChatMessage{type=ChatType.CHAT_TYPE_GUILD,senderUid=p.Uid,senderCommonInfo=n.commonInfo,leaderCardInfo=n.leaderCardInfo,message=message,timeStamp=Now,chatSeq=++g.ChatSequence};g.Chat.Add(Convert.ToBase64String(WireCodec.Serialize(msg)));if(g.Chat.Count>200)g.Chat.RemoveAt(0);var r=new GsSendGuildChatMessageOk();r.msgList.AddRange(GuildMessages(g,chat.lastMsgSeq));return ok(r);
        }
        var battleReply=GuildBattleOperation(name,request,p,ok,fail);if(battleReply!=null)return battleReply;
        if(name=="GS_GET_GUILD_RVR_BATTLE_RANK_INFO_REQUEST")return ok(new GsGetGuildRvRBattleRankInfoOk());
        if(name=="GS_GET_GUILD_RVR_BATTLE_LOBBYINFO_REQUEST")return ok(new GsGetGuildRvRBattleLobbyInfoOk{rvrBattleState=RVR_BATTLE_STATE.RVR_BATTLE_SERVER_STATE_NONE,ableToJoin=false});
        if(name.Contains("GUILD_RVR",StringComparison.Ordinal)||name is "GS_GET_GUILD_BATTLE_ENEMY_DECK_INFO_REQUEST" or "GS_UPDATE_GUILD_BATTLE_RESULT_REQUEST")return Bad("local guilds have no matched online opponent",ServerErrorCode.ERROR_GUILD_RVR_BATTLE_NO_MATCHED_GUILD);
        if(name=="GS_UPDATE_GUILD_PLATFORM_GROUP_ID_REQUEST")return Bad("QQ and WeChat group binding is unavailable in local mode");
        return null;
    }
}
