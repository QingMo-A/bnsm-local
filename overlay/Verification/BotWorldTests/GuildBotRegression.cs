using System.Security.Cryptography;
using System.Text.Json;

internal static class GuildBotRegression
{
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        foreach(var bot in runtime.Router.Bots.All().Take(6))p.Friends.Links[bot.Uid]=new(){Joined=ProtocolRouter.Now};
        using(var peer=new Peer(root)){await peer.Login(runtime,p.Account);await peer.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,new GsCreateGuildRequest{guildName="同门测试",joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17});}
        runtime.Store.Save();File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,guildId=p.Guild.GuildId,botUid=runtime.Router.Bots.All().First().Uid,normalSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","guild-bots-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool pass,string note){if(!pass)throw new InvalidDataException(note);checks.Add(note);Console.WriteLine("PASS "+note);}
        uint guildId=0,uid=0;uint contributed=0;ulong attendance=0;string residents="";
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var owner=runtime.Store.All().Single();var other=runtime.Store.GetOrCreate("other-guild-owner");other.Level=99;other.CashPoint=1000000;
            var ordinary=runtime.Store.GetOrCreate("ordinary-member");ordinary.Level=99;
            using var a=new Peer(root);using var b=new Peer(root);using var c=new Peer(root);await a.Login(runtime,owner.Account);await b.Login(runtime,other.Account);await c.Login(runtime,ordinary.Account);
            GsCreateGuildRequest Create(string name)=>new(){guildName=name,joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17};
            var made=await a.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("人机门派"));guildId=made.guildInfo.guildID;
            var guild=owner.FoundedGuilds.Single();var second=await b.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("另一门派"));
            await c.Request<GsJoinGuildOk>(GameServerCommand.GS_JOIN_GUILD_REQUEST,new GsJoinGuildRequest{guildID=guildId});
            uid=runtime.Router.Bots.All().OrderBy(x=>x.Uid).First().Uid;residents=JsonSerializer.Serialize(runtime.Router.Bots.All());
            string State()=>JsonSerializer.Serialize(runtime.Store.All().Select(p=>new{p.Gold,p.CashPoint,p.Inventory,p.GuildTokens,p.Guild,p.FoundedGuilds,p.GuildBotActivities}));
            var invite=new GsInviteGuildRequest{inviteUid=uid};string before=State();
            await c.Request<FailPacket>(GameServerCommand.GS_INVITE_GUILD_REQUEST,invite,success:false);Check(State()==before,"An ordinary member cannot recruit NPCs or mutate guild state");
            await a.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,invite);
            Check(guild.Members.ContainsKey(uid)&&guild.Members[uid].IsBot&&!guild.Invitees.Contains(uid),"An eligible resident accepts the native invitation and occupies one normal member slot");
            before=State();await a.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,invite,retry:true);Check(State()==before,"Invitation retry cannot add a second member or reset activity schedules");
            before=State();await b.Request<FailPacket>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=uid},success:false);Check(State()==before,"The same resident cannot join two guilds");
            ulong humanLastSeen=ProtocolRouter.Now-3*86400;ordinary.LastActivity=humanLastSeen;
            var roster=await a.Request<GsGetMyGuildUserInfoListOk>(GameServerCommand.GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST,new GsGetMyGuildUserInfoListRequest());
            Check(roster.totalDataCount==3&&roster.dataCount==3&&roster.guildUserInfoList.Any(x=>x.guildUserData.uid==uid&&x.guildUserData.nick.Contains("人机")),"Native member packets include the NPC profile and terminate at the real member count");
            bool Online(ulong time)=>time>0&&time<=ProtocolRouter.Now&&ProtocolRouter.Now-time<10;
            Check(Online(roster.guildUserInfoList.Single(x=>x.guildUserData.uid==uid).guildUserData.logoutTime),"A legacy NPC with no login heartbeat is online in the native guild roster");
            Check(roster.guildUserInfoList.Single(x=>x.guildUserData.uid==ordinary.Uid).guildUserData.logoutTime==humanLastSeen,"Human guild members retain their actual last activity instead of inheriting NPC presence");
            owner.Friends.Links[uid]=new(){Joined=ProtocolRouter.Now};
            var friends=await a.Request<GsGetFriendListOk>(GameServerCommand.GS_GET_FRIEND_LIST_REQUEST,new GsGetFriendListRequest{friendStatus=FriendStatus.FRIEND_STATUS_FRIEND_DONE});
            Check(Online(friends.friendList.Single(x=>x.uid==uid).logoutTime),"The native friend list shares the resident's current online presence");
            var search=await a.Request<GsFindFriendOk>(GameServerCommand.GS_FIND_FRIEND_REQUEST,new GsFindFriendRequest{findNick=uid.ToString()});
            Check(Online(search.findFriendList.Single(x=>x.uid==uid).logoutTime),"Friend search returns current resident presence without a stored zero timestamp");
            var recommendations=await a.Request<GsRecommendFriendOk>(GameServerCommand.GS_RECOMMEND_FRIEND_REQUEST,new GsRecommendFriendRequest());
            Check(recommendations.recommendList.Where(x=>runtime.Router.Bots.Find(x.uid)!=null).Any()&&recommendations.recommendList.Where(x=>runtime.Router.Bots.Find(x.uid)!=null).All(x=>Online(x.logoutTime)),"Recommended residents also use current server presence");
            Check(JsonSerializer.Serialize(runtime.Router.Bots.All())==residents,"Presence reads cannot rewrite resident identities, loadouts or activity fields");
            var profile=await a.Request<GsGetAnotherUserGameInfoOk>(GameServerCommand.GS_GET_ANOTHER_USER_GAME_INFO_REQUEST,new GsGetAnotherUserGameInfoRequest{targetUid=uid});Check(profile.nameCardInfo.guildName==guild.Name,"The NPC's native player profile displays its actual joined guild");
            var recruit=runtime.Router.Bots.All().First(x=>x.Uid!=uid&&x.Profile.Level<99);
            guild.MinLevel=99;before=State();await a.Request<FailPacket>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=recruit.Uid},success:false);Check(State()==before,"Recruitment retains the original minimum-level gate");guild.MinLevel=17;
            guild.JoinType=GuildJoinType.GUILD_JOIN_TYPE_UNABLE;before=State();await a.Request<FailPacket>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=recruit.Uid},success:false);Check(State()==before,"A guild closed to new members cannot silently auto-accept an NPC");guild.JoinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN;
            var dummy=new List<uint>();int capacity=new GuildCatalog(root).Levels.Single(x=>x.GuildLevel==guild.Level).MaxUserCount;
            for(uint extra=10000000;guild.Members.Count<capacity;extra++){guild.Members[extra]=new(){Joined=ProtocolRouter.Now,Week=(ProtocolRouter.TowerDay(ProtocolRouter.Now)+3)/7};dummy.Add(extra);}
            before=State();await a.Request<FailPacket>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=recruit.Uid},success:false);Check(State()==before,"A full guild rejects NPC invitations before membership or activity-ledger mutation");foreach(uint extra in dummy)guild.Members.Remove(extra);
            before=State();await a.Request<FailPacket>(GameServerCommand.GS_UPDATE_GUILD_USER_GRADE_REQUEST,new GsUpdateGuildUserGradeRequest{targetUid=uid,changeGrade=GuildUserGrade.GUILD_USER_GRADE_MASTER},success:false);Check(State()==before,"Leadership cannot be stranded on a noninteractive NPC");
            await a.Request<object>(GameServerCommand.GS_UPDATE_GUILD_USER_GRADE_REQUEST,new GsUpdateGuildUserGradeRequest{targetUid=uid,changeGrade=GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER});
            Check(guild.Members[uid].Grade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER,"NPC member administration supports the original deputy grade");
            await a.Request<GsCheckGuildAttendanceOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,new GsCheckGuildAttendanceRequest());
            ulong now=ProtocolRouter.Now;var activity=owner.GuildBotActivities[uid];activity.AttendanceAt=now;activity.ContributionAt=now;
            uint points=guild.Points,count=guild.DonationCount,gold=owner.Gold,cash=owner.CashPoint,tokens=owner.GuildTokens;string inventory=JsonSerializer.Serialize(owner.Inventory);
            runtime.Router.GuildBotTick(now);
            Check(guild.Members[uid].AttendanceDay==guild.AttendanceDay&&activity.AttendanceDay==guild.AttendanceDay,"The NPC checks in for the current native attendance cycle");
            contributed=guild.Members[uid].Contribution;Check(contributed>0&&guild.Points==points+contributed&&guild.DonationCount==count+1&&guild.Members[uid].WeeklyContribution==contributed,"One guild errand consumes its voucher and credits the original contribution amount exactly once");
            Check(activity.Supplies.Values.All(x=>x==0)&&activity.ContributionDay==ProtocolRouter.TowerDay(now),"NPC-only daily supplies are consumed and the daily contribution watermark is persisted");
            Check(owner.Gold==gold&&owner.CashPoint==cash&&owner.GuildTokens==tokens&&JsonSerializer.Serialize(owner.Inventory)==inventory,"NPC activity cannot spend human currencies, consume human items or grant human tokens");
            var info=await a.Request<GsGetMyGuildInfoOk>(GameServerCommand.GS_GET_MY_GUILD_INFO_REQUEST,new GsGetMyGuildInfoRequest());Check(info.todayAttendanceCount==2,"The player's native attendance count includes the signed-in NPC");
            before=State();runtime.Router.GuildBotTick(now);runtime.Router.GuildBotTick(now-1);Check(State()==before,"Repeated ticks and clock rollback cannot duplicate attendance or donations");
            await a.Request<object>(GameServerCommand.GS_BAN_GUILD_USER_REQUEST,new GsBanGuildUserRequest{targetUid=uid});Check(!guild.Members.ContainsKey(uid)&&activity.User.GuildId==0,"Removing an NPC clears its authoritative membership without editing bot-world");
            var removedProfile=await a.Request<GsGetAnotherUserGameInfoOk>(GameServerCommand.GS_GET_ANOTHER_USER_GAME_INFO_REQUEST,new GsGetAnotherUserGameInfoRequest{targetUid=uid});Check(removedProfile.nameCardInfo.guildName=="","Removing an NPC clears the guild name from its native player profile");
            before=JsonSerializer.Serialize(activity);runtime.Router.GuildBotTick(now+1);Check(JsonSerializer.Serialize(activity)==before,"Removed NPCs stop doing work for the old guild");
            await b.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=uid});var otherGuild=other.FoundedGuilds.Single();activity.AttendanceAt=now;activity.ContributionAt=now;
            points=otherGuild.Points;runtime.Router.GuildBotTick(now+2);Check(otherGuild.Points==points&&otherGuild.Members[uid].Contribution==0&&otherGuild.Members[uid].AttendanceDay!=otherGuild.AttendanceDay,"Kicking and re-inviting across guilds cannot farm a second daily contribution or check-in");
            ulong next=now+86400;runtime.Router.GuildBotTick(next);
            Check(otherGuild.Members[uid].Contribution>0&&activity.ContributionDay==ProtocolRouter.TowerDay(next),"A later day permits the new guild's normal daily activity");
            uint beforeSkip=otherGuild.Members[uid].Contribution,donationCount=otherGuild.DonationCount;runtime.Router.GuildBotTick(next+7*86400);
            Check(otherGuild.DonationCount==donationCount+1&&otherGuild.Members[uid].Contribution==beforeSkip*2,"Returning after a week grants only the current day's work, not seven fabricated offline donations");
            Check(otherGuild.Members[uid].WeeklyContribution==beforeSkip,"Weekly contribution resets separately from lifetime contribution");
            attendance=activity.AttendanceDay;contributed=otherGuild.Members[uid].Contribution;
            Check(JsonSerializer.Serialize(runtime.Router.Bots.All())==residents,"Guild work leaves resident identities, combat loadouts and world inventories unchanged");
            runtime.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var owner=restarted.Store.All().Single(p=>p.Account=="other-guild-owner");var guild=owner.FoundedGuilds.Single();using var peer=new Peer(root);await peer.Login(restarted,owner.Account);
            var roster=await peer.Request<GsGetMyGuildUserInfoListOk>(GameServerCommand.GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST,new GsGetMyGuildUserInfoListRequest());
            Check(roster.guildUserInfoList.Any(x=>x.guildUserData.uid==uid&&x.ContributionPoint==contributed),"Restart restores the NPC roster and its exact saved contribution");
            ulong presence=roster.guildUserInfoList.Single(x=>x.guildUserData.uid==uid).guildUserData.logoutTime;
            Check(presence>0&&presence<=ProtocolRouter.Now&&ProtocolRouter.Now-presence<10,"Restart derives fresh NPC presence while retaining legacy saved profiles");
            var activity=restarted.Store.All().Select(p=>p.GuildBotActivities.GetValueOrDefault(uid)).First(x=>x!=null)!;
            Check(activity.AttendanceDay==attendance&&activity.User.GuildId==guild.Id,"One authoritative activity ledger follows a resident across guild transfer and restart");
            string before=JsonSerializer.Serialize(guild);restarted.Router.GuildBotTick();Check(JsonSerializer.Serialize(guild)==before,"Restart and the live timer cannot replay the already completed daily work");
        }
        await using(var live=new LocalRuntime(root,Path.Combine(folder,"timer-saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(live);var owner=live.Store.All().Single();using var peer=new Peer(root);await peer.Login(live,owner.Account);
            await peer.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,new GsCreateGuildRequest{guildName="后台日常",joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17});
            uint bot=live.Router.Bots.All().First().Uid;await peer.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=bot});
            var activity=owner.GuildBotActivities[bot];activity.AttendanceAt=activity.ContributionAt=ProtocolRouter.Now;var guild=owner.FoundedGuilds.Single();
            var until=DateTime.UtcNow.AddSeconds(10);while(guild.Members[bot].Contribution==0&&DateTime.UtcNow<until)await Task.Delay(100);
            Check(guild.Members[bot].Contribution>0&&guild.Members[bot].AttendanceDay==guild.AttendanceDay,"The real background timer performs NPC check-in and contribution without a guild-page request");
            lock(live.Store.Gate){var saved=JsonSerializer.Deserialize<List<LocalPlayer>>(File.ReadAllText(Path.Combine(folder,"timer-saves/players.json")))!.Single();Check(saved.FoundedGuilds.Single().Members[bot].Contribution==guild.Members[bot].Contribution,"The timer commits its guild activity and membership together to the durable player save");}
        }
        var report=new{passed=true,checks,folder,normalSaveUsed=false,runtimeTimerVerified=true,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()};
        File.WriteAllText("I:/BnsmModernWork/Verification/guild-bots-server-latest.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("GUILD_BOTS_PASSED "+checks.Count);
    }
}
