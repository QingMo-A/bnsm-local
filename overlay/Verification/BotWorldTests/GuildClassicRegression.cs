using System.Net.Sockets;
using System.Text.Json;
internal static class GuildTest
{
    private sealed class Peer(string root) : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();public byte[] Last=[];
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server,string account="local-player")
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),(await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload)!;
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false)
        {
            if(!retry)Key.sequenceNo++;body.GetType().GetField("keyInfo")?.SetValue(body,Key);
            var result=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)result.Command).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)result.Command+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),result.Payload)!).message);
            Last=result.Payload;if(typeof(T)==typeof(object))return(T)new object();
            var decoded=WireCodec.Deserialize(typeof(T),Last)!;
            if(decoded is GsGetMyGuildUserInfoListOk roster)while(roster.dataCount<roster.totalDataCount){
                using var timeout=new CancellationTokenSource(10000);var next=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);
                if(next.Command!=result.Command)throw new Exception("Unexpected roster continuation");
                var part=(GsGetMyGuildUserInfoListOk)WireCodec.Deserialize(typeof(GsGetMyGuildUserInfoListOk),next.Payload)!;roster.guildUserInfoList.AddRange(part.guildUserInfoList);roster.dataCount+=part.dataCount;
            }
            return(T)decoded;
        }
        public Task<LocalEquipmentReply> Batch(LocalEquipmentRequest body)=>Request<LocalEquipmentReply>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,body);
        public void Dispose()=>socket.Dispose();
    }

    private static LocalPlayer Prepare(LocalRuntime r,string account="local-player",uint level=99)
    {
        var p=r.Store.GetOrCreate(account);ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;p.Level=level;p.Exp=(uint)r.Catalog.PlayerStates.Single(x=>x.Level==level).Exp;p.Gold=1000000;p.CashPoint=900000;
        var ticket=r.Catalog.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_CONTRIBUTION);p.Inventory[(uint)ticket.Id]=30;r.Store.Save();return p;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmModernWork\Verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(r.Store.All().Count==0){var p=Prepare(r);p.Guild.AttendanceRewards.Add(new(){GuildId=1,DataId=1});var friend=Prepare(r,"guild-native-friend",50);friend.Nickname="测试同门";
            var tier=r.Catalog.Shops.Single(x=>x.ShopId==5).GoodsIdList.Single();var market=new LocalMarketState{Day=ProtocolRouter.TowerDay(ProtocolRouter.Now),NextRefresh=(ProtocolRouter.TowerDay(ProtocolRouter.Now)+1)*86400-4*3600,Tier=2};
            var offers=r.Catalog.Goods.Where(x=>x.GuildT>=10&&!x.GuildPresent&&x.ProductIdType==BscTable.GOODS_TYPE.GOODS_TYPE_ITEM&&x.Quantity>0&&x.GuildLevelMin==0&&x.GuildLevelMax==0&&x.PcLevelMin<=p.Level&&x.PcLevelMax>=p.Level&&r.Catalog.Items.Any(i=>i.Id==x.ProductId)).OrderBy(x=>x.GuildT).ThenBy(x=>x.GoodsId).Take(8).ToArray();
            for(int i=0;i<offers.Length;i++)market.Slots.Add(new(){GoodsId=(uint)offers[i].GoodsId,Vip=tier.VipLevel[i],Discount=tier.DiscountRatio[i]});p.Markets[5]=market;r.Store.Save();}
        await using var app=await DebugToolsTest.Host(r);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"proof.json"),JsonSerializer.Serialize(new{ticketId=r.Catalog.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_CONTRIBUTION).Id,expectedGuildTokens=r.Store.GetOrCreate("local-player").GuildTokens}));
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        var stop=DateTime.UtcNow.AddMinutes(30);while(DateTime.UtcNow<stop){await Task.Delay(200);lock(r.Store.Gate){
            var owner=r.Store.GetOrCreate("local-player");var guild=owner.FoundedGuilds.FirstOrDefault(x=>!x.Disbanded);var friend=r.Store.GetOrCreate("guild-native-friend");
            if(guild!=null&&!guild.Members.ContainsKey(friend.Uid)){guild.Members[friend.Uid]=new(){Joined=ProtocolRouter.Now};friend.Guild.GuildId=guild.Id;r.Store.Save();}
        }}
    }
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        string path=Path.Combine(@"I:\BnsmModernWork\Verification","guild-protocol-"+Guid.NewGuid().ToString("N"));uint guildId=0;
        await using(var r=new LocalRuntime(root,path,0,0,0,false)){
            var p=Prepare(r);var q=Prepare(r,"local-friend",50);var third=Prepare(r,"local-third",40);var catalog=new GuildCatalog(root);
            using var a=new Peer(root);using var b=new Peer(root);using var c=new Peer(root);await a.Login(r);await b.Login(r,q.Account);await c.Login(r,third.Account);
            string State()=>JsonSerializer.Serialize(new{players=r.Store.All().Select(x=>new{x.Gold,x.CashPoint,x.GuildTokens,x.Inventory,x.EquipmentItems,x.Guild,x.FoundedGuilds,x.CardIds})});
            async Task Bad(Peer peer,GameServerCommand cmd,object body,string text){string old=State();await peer.Request<FailPacket>(cmd,body,false);Check(State()==old,text+" rejects without inventory or membership mutation");}
            var none=await a.Request<GsGetMyGuildInfoOk>(GameServerCommand.GS_GET_MY_GUILD_INFO_REQUEST,new GsGetMyGuildInfoRequest());Check(none.status==GuildJoinStatus.GUILD_JOIN_STATUS_NONE&&none.guildInfo!=null,"Legacy saves start without phantom membership");
            var recommend=await a.Request<GsGetRecommendGuildListOk>(GameServerCommand.GS_GET_RECOMMEND_GUILD_LIST_REQUEST,new GsGetRecommendGuildListRquest());Check(recommend.resultCount==0,"No fabricated online guilds");
            GsCreateGuildRequest Create(string name)=>new(){guildName=name,guildGreeting="本地门派",joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17};
            await Bad(a,GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("a"),"Invalid guild name");
            p.Level=1;await Bad(a,GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("青龙堂"),"Original guild opening level");p.Level=99;
            p.CashPoint=499;await Bad(a,GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("青龙堂"),"Original 500-diamond creation cost");p.CashPoint=900000;
            var made=await a.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("青龙堂"));guildId=made.guildInfo.guildID;Check(made.guildInfo.userCount==1&&made.guildInfo.guildLevel==1&&made.grade==GuildUserGrade.GUILD_USER_GRADE_MASTER&&made.currentCoinInfo.cashPoint==899500,"Create returns original cost, membership and leader grade");
            string afterCreate=State();await a.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("青龙堂"),retry:true);Check(State()==afterCreate,"Create replay does not charge or create twice");
            await a.Request<FailPacket>(GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("白虎堂"),false,true);Check(State()==afterCreate,"Altered create replay is rejected");
            await Bad(b,GameServerCommand.GS_CREATE_GUILD_REQUEST,Create("青龙堂"),"Duplicate guild name");
            var g=p.FoundedGuilds.Single();var joined=await b.Request<GsJoinGuildOk>(GameServerCommand.GS_JOIN_GUILD_REQUEST,new GsJoinGuildRequest{guildID=guildId});Check(joined.status==GuildJoinStatus.GUILD_JOIN_STATUS_COMPLETE&&g.Members.Count==2,"Open guild joins real local profile");
            var roster=await a.Request<GsGetMyGuildUserInfoListOk>(GameServerCommand.GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST,new GsGetMyGuildUserInfoListRequest());Check(roster.totalDataCount==2&&roster.dataCount==2&&roster.guildUserInfoList.All(x=>x.guildUserData.leaderCardInfo!=null),"Legacy multi-part roster terminates with complete native profiles");
            await Bad(b,GameServerCommand.GS_UPDATE_GUILD_GREETING_REQUEST,new GsUpdateGuildGreetingRequest{guildID=guildId,guildGreeting="越权"},"Ordinary member cannot edit notice");
            await a.Request<object>(GameServerCommand.GS_UPDATE_GUILD_GREETING_REQUEST,new GsUpdateGuildGreetingRequest{guildID=guildId,guildGreeting="欢迎回家"});Check(g.Greeting=="欢迎回家","Edit guild notice");
            await a.Request<object>(GameServerCommand.GS_UPDATE_GUILD_USER_GRADE_REQUEST,new GsUpdateGuildUserGradeRequest{targetUid=q.Uid,changeGrade=GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER});Check(g.Members[q.Uid].Grade==GuildUserGrade.GUILD_USER_GRADE_SUB_MASTER,"Promote deputy");
            await b.Request<object>(GameServerCommand.GS_UPDATE_GUILD_GREETING_REQUEST,new GsUpdateGuildGreetingRequest{guildID=guildId,guildGreeting="副掌门公告"});Check(g.Greeting=="副掌门公告","Deputy can edit notice");
            var settings=new GsChangeGuildInfoRequest{emblemID=1001,joinMinLevel=17,joinType=GuildJoinType.GUILD_JOIN_TYPE_CLOSE};await a.Request<GsChangeGuildInfoOk>(GameServerCommand.GS_CHANGE_GUILD_INFO_REQUEST,settings);Check(g.Emblem==1001&&g.JoinType==GuildJoinType.GUILD_JOIN_TYPE_CLOSE,"Update emblem and approval policy");
            var waiting=await c.Request<GsJoinGuildOk>(GameServerCommand.GS_JOIN_GUILD_REQUEST,new GsJoinGuildRequest{guildID=guildId});Check(waiting.status==GuildJoinStatus.GUILD_JOIN_STATUS_WAITING&&!g.Members.ContainsKey(third.Uid),"Closed guild waits for approval");
            var applicants=await a.Request<GsGetGuildJoinWaitingListOk>(GameServerCommand.GS_GET_GUILD_JOIN_WAITING_LIST_REQUEST,new GsGetGuildJoinWaitingListRequest());Check(applicants.userDataList.Single().uid==third.Uid,"Application roster uses original packet");
            await b.Request<object>(GameServerCommand.GS_DECISION_JOIN_GUILD_REQUEST,new GsDecisionJoinGuildRequest{targetUid=third.Uid,decision=1});Check(g.Members.Count==3&&third.Guild.GuildId==guildId,"Deputy approves real application");
            await c.Request<object>(GameServerCommand.GS_WITHDRAW_GUILD_REQUEST,new GsWithDrawGuildRequest());Check(!g.Members.ContainsKey(third.Uid)&&third.Guild.WithdrawTime>0,"Ordinary member withdraws");
            await a.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=third.Uid});var invites=await c.Request<GsGetGuildInvitedListOk>(GameServerCommand.GS_GET_GUILD_INVITED_LIST_REQUEST,new GsGetGuildInvitedListRequest());Check(invites.totalDataCount==1&&invites.dataCount==1,"Legacy invitation list terminates");
            await c.Request<object>(GameServerCommand.GS_DENY_GUILD_INVITE_REQUEST,new GsDenyGuildInviteRequest{denyGuildId=guildId});Check(!g.Invitees.Contains(third.Uid),"Deny invitation");
            await a.Request<object>(GameServerCommand.GS_INVITE_GUILD_REQUEST,new GsInviteGuildRequest{inviteUid=third.Uid});await c.Request<GsAcceptGuildInviteOk>(GameServerCommand.GS_ACCEPT_GUILD_INVITE_REQUEST,new GsAcceptGuildInviteRequest{acceptGuildId=guildId});Check(g.Members.Count==3,"Accept invitation");
            await Bad(c,GameServerCommand.GS_UPDATE_GUILD_USER_GRADE_REQUEST,new GsUpdateGuildUserGradeRequest{targetUid=p.Uid,changeGrade=GuildUserGrade.GUILD_USER_GRADE_NORMAL},"Ordinary member cannot change leader grade");
            await Bad(a,GameServerCommand.GS_WITHDRAW_GUILD_REQUEST,new GsWithDrawGuildRequest(),"Leader cannot orphan guild");
            await Bad(a,GameServerCommand.GS_DISBAND_GUILD_REQUEST,new GsDisbandGuildRequest(),"Disband requires sole member");
            var attendance=await a.Request<GsCheckGuildAttendanceOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,new GsCheckGuildAttendanceRequest());Check(attendance.todayAttendanceCount==1,"Check in once");
            await Bad(a,GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,new GsCheckGuildAttendanceRequest(),"Repeat check-in");
            await Bad(a,GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST,new GsRecvGuildAttendanceRewardRequest(),"Attendance reward unavailable before settlement");
            g.AttendanceDay--;g.Members[p.Uid].AttendanceDay=g.AttendanceDay;p.Guild.AttendanceDay=g.AttendanceDay;
            var settled=await a.Request<GsGetMyGuildInfoOk>(GameServerCommand.GS_GET_MY_GUILD_INFO_REQUEST,new GsGetMyGuildInfoRequest());Check(settled.attendanceRewardID==1&&settled.yesterdayAttendanceCount==1&&!settled.attendanceFlag,"22:02 attendance settlement selects original one-person reward");
            uint gold=p.Gold;var award=await a.Request<GsRecvGuildAttendanceRewardOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST,new GsRecvGuildAttendanceRewardRequest());Check(p.Gold==gold+50000&&award.rewardItemList.Count==2&&award.rewardDataId==1,"Attendance gives exact original gold and materials");
            afterCreate=State();await a.Request<GsRecvGuildAttendanceRewardOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST,new GsRecvGuildAttendanceRewardRequest(),retry:true);Check(State()==afterCreate,"Attendance replay cannot duplicate rewards");
            await Bad(a,GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REWARD_REQUEST,new GsRecvGuildAttendanceRewardRequest(),"Repeated attendance claim");
            var info=await a.Request<GsGetGuildContributionInfoOk>(GameServerCommand.GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST,new GsGetGuildContributionInfoRequest());Check(info.slotSeqList.Count==5&&info.currentSlotId==0,"Five original weighted donation categories");
            await b.Request<GsGetGuildContributionInfoOk>(GameServerCommand.GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST,new GsGetGuildContributionInfoRequest());
            uint beforeReset=q.CashPoint;await b.Request<GsGuildContributionResetOk>(GameServerCommand.GS_GUILD_CONTRIBUTION_RESET_REQUEST,new GsGuildContributionResetRequest());Check(q.Guild.CurrentSlot==0&&q.Guild.Resets==1&&q.CashPoint==beforeReset,"Native reset may discard unfinished board for its original free daily reset");
            await Bad(a,GameServerCommand.GS_GUILD_CONTRIBUTION_REQUEST,new GsGuildContributionRequest{itemInfo=new(){itemID=0,itemCount=1}},"Invalid donation material");
            uint ticket=(uint)r.Catalog.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_CONTRIBUTION).Id;
            for(int cycle=0;cycle<2;cycle++){
                for(int i=0;i<5;i++){
                    uint points=g.Points,balance=p.GuildTokens;int count=p.Inventory[ticket];var slot=catalog.Contributions.Single(x=>x.Id==info.guildContributionDataId).ContributionSlotInfoList.Single(x=>x.SlotSeq==info.slotSeqList[i]);
                    var body=new GsGuildContributionRequest{itemInfo=new(){itemID=ticket,itemCount=1}};var response=await a.Request<GsGuildContributionOk>(GameServerCommand.GS_GUILD_CONTRIBUTION_REQUEST,body);
                    Check(p.Inventory[ticket]==count-1&&g.Points==points+slot.RewardGuildPoint&&p.GuildTokens==balance+slot.RewardGuildPoint&&response.earnedCoinInfo.guildToken==slot.RewardGuildPoint,$"Donation {cycle}/{i} consumes one ticket, grants original points/gold plus declared local tokens");
                    string state=State();await a.Request<GsGuildContributionOk>(GameServerCommand.GS_GUILD_CONTRIBUTION_REQUEST,new GsGuildContributionRequest{itemInfo=new(){itemID=ticket,itemCount=1}},retry:true);Check(State()==state,$"Donation {cycle}/{i} replay is idempotent");
                }
                await Bad(a,GameServerCommand.GS_GUILD_CONTRIBUTION_REQUEST,new GsGuildContributionRequest{itemInfo=new(){itemID=ticket,itemCount=1}},"Completed donation board");
                if(cycle==0){uint cash=p.CashPoint;var reset=await a.Request<GsGuildContributionResetOk>(GameServerCommand.GS_GUILD_CONTRIBUTION_RESET_REQUEST,new GsGuildContributionResetRequest());Check(reset.slotSeqList.Count==5&&reset.currentSlotId==0&&p.CashPoint==cash,"Original money-type NONE gives one free reset and five new slots");info=await a.Request<GsGetGuildContributionInfoOk>(GameServerCommand.GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST,new GsGetGuildContributionInfoRequest());}
            }
            await Bad(a,GameServerCommand.GS_GUILD_CONTRIBUTION_RESET_REQUEST,new GsGuildContributionResetRequest(),"Original one-reset daily limit");
            var missions=await a.Request<GsGetGuildMissionListOk>(GameServerCommand.GS_GET_GUILD_MISSION_LIST_REQUEST,new GsGetGuildMissionListRequest());Check(missions.list.Single().missionId==7200&&missions.list.Single().status==MissionStatus.MISSION_STATUS_COMPLETED,"Original donation mission counts donations, not point totals");
            await Bad(b,GameServerCommand.GS_GET_GUILD_MISSION_REWARD_REQUEST,new GsGetGuildMissionRewardRequest{missionId=7200},"Deputy cannot claim leader-only achievement");
            await a.Request<GsGetGuildMissionRewardOk>(GameServerCommand.GS_GET_GUILD_MISSION_REWARD_REQUEST,new GsGetGuildMissionRewardRequest{missionId=7200});Check(g.Points==1110&&g.ClaimedMissions.Contains(7200),"Guild achievement reward and points");
            await Bad(a,GameServerCommand.GS_GET_GUILD_MISSION_REWARD_REQUEST,new GsGetGuildMissionRewardRequest{missionId=7200},"Achievement cannot be claimed twice");
            await a.Request<GsGuildLevelUpOk>(GameServerCommand.GS_GUILD_LEVEL_UP_REQUEST,new GsGuildLevelUpRequest());Check(g.Level==2&&g.Points==110,"Original guild upgrade costs 1000 points");
            await Bad(a,GameServerCommand.GS_GUILD_LEVEL_UP_REQUEST,new GsGuildLevelUpRequest(),"Next guild upgrade requires original 25000 points");
            var worship=await b.Request<GsGuildWorshipOk>(GameServerCommand.GS_GUILD_WORSHIP_REQUEST,new GsGuildWorshipRequest{targetUid=p.Uid,worshipType=BscTable.GUILD_WORSHIP_TYPE.WORSHIP_TYPE_FREE});Check(worship.stamina==125&&p.Guild.WorshipGold==1000,"Free worship gives original 25 stamina and 1000 recipient gold");
            await Bad(b,GameServerCommand.GS_GUILD_WORSHIP_REQUEST,new GsGuildWorshipRequest{targetUid=p.Uid,worshipType=BscTable.GUILD_WORSHIP_TYPE.WORSHIP_TYPE_FREE},"Cannot worship same member twice");
            await Bad(a,GameServerCommand.GS_GUILD_WORSHIP_REQUEST,new GsGuildWorshipRequest{targetUid=q.Uid},"Cannot worship lower level");
            await c.Request<GsGuildWorshipOk>(GameServerCommand.GS_GUILD_WORSHIP_REQUEST,new GsGuildWorshipRequest{targetUid=p.Uid,worshipType=BscTable.GUILD_WORSHIP_TYPE.WORSHIP_TYPE_GOLD});Check(third.Gold==970000&&third.Stamina==150&&p.Guild.WorshipGold==6000,"Paid worship costs 30000 and gives original stamina/income");
            gold=p.Gold;await a.Request<GsReceiveGuildWorshipedRewardOk>(GameServerCommand.GS_RECEIVE_GUILD_WORSHIPED_REWARD_REQUEST,new GsReceiveGuildWorshipedRewardRequest());Check(p.Gold==gold+6000&&p.Guild.WorshipGold==0,"Receive worship income once");
            await Bad(a,GameServerCommand.GS_RECEIVE_GUILD_WORSHIPED_REWARD_REQUEST,new GsReceiveGuildWorshipedRewardRequest(),"Cannot reclaim worship income");
            var pageRequest=new GsGetIngameShopSellingListRequest();pageRequest.shopId.Add(5);var shop=await a.Request<GsGetIngameShopSellingListOk>(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,pageRequest);var page=shop.shopSetDataList.Single();Check(page.goodsList.Count==8&&page.goodsList.All(x=>x.moneytype==BscTable.MONEY_TYPE.MONEY_TYPE_GUILD_TOKEN),"Native guild shop has eight original priced offers");
            p.GuildTokens=uint.MaxValue-10000;var first=page.goodsList[0];uint tokens=p.GuildTokens;var purchase=new LocalShopBuyRequest{shopId=5,packageId=first.packageId,SlotIndex=0};var bought=await a.Request<GsBuyIngameShopGoodsOk>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase);uint nativeCost=(uint)(int)(r.Catalog.Goods.Single(x=>x.GoodsId==first.packageId).GuildT*33d*0.01);Check(p.GuildTokens==tokens-nativeCost&&bought.deltaItemList.Count>0&&bought.shopSetData.goodsList[0].curStock==1,"Guild shop spends tokens and stores original item");
            await Bad(a,GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase,"Guild-shop stock cannot be bought twice");
            await Bad(a,GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,new LocalShopBuyRequest{shopId=5,packageId=page.goodsList[4].packageId,SlotIndex=4},"Guild-shop slot requires original guild level four");
            var sent=await a.Request<GsSendGuildChatMessageOk>(GameServerCommand.GS_SEND_GUILD_CHAT_MESSAGE_REQUEST,new GsSendGuildChatMessageRequest{msg="欢迎回来"});Check(sent.msgList.Single().senderUid==p.Uid,"Guild chat stores real local author");
            var chatList=await b.Request<GsFetchGuildChatMessageOk>(GameServerCommand.GS_FETCH_GUILD_CHAT_MESSAGE_REQUEST,new GsFetchGuildChatMessageRequest());Check(chatList.msgList.Single().message=="欢迎回来","Another local member can fetch guild chat");
            await a.Request<object>(GameServerCommand.GS_UPDATE_GUILD_USER_GRADE_REQUEST,new GsUpdateGuildUserGradeRequest{targetUid=q.Uid,changeGrade=GuildUserGrade.GUILD_USER_GRADE_MASTER});Check(g.Master==q.Uid&&g.Members[p.Uid].Grade==GuildUserGrade.GUILD_USER_GRADE_NORMAL,"Leadership transfer updates both members atomically");
            await b.Request<object>(GameServerCommand.GS_BAN_GUILD_USER_REQUEST,new GsBanGuildUserRequest{targetUid=third.Uid});Check(third.Guild.GuildId==0&&third.Guild.Latest==LatestMyGuildStatusFlag.LATEST_MY_GUILD_STATUS_BAN,"Leader removes member and records status");
            afterCreate=State();await b.Request<object>(GameServerCommand.GS_BAN_GUILD_USER_REQUEST,new GsBanGuildUserRequest{targetUid=third.Uid},retry:true);Check(State()==afterCreate,"Empty acknowledgement mutation replay is idempotent");
            var history=await b.Request<GsGetGuildHistoryOk>(GameServerCommand.GS_GET_GUILD_HISTORY_REQUEST,new GsGetGuildHistoryRequest());Check(history.historyList.Any(x=>x.logType==GuildHistoryLogType.GUILD_HISTORY_GUILD_LEVEL_UP)&&history.totalCount==history.historyList.Count,"Original history renders real local events");
            var lobby=await b.Request<GsGetGuildRvRBattleLobbyInfoOk>(GameServerCommand.GS_GET_GUILD_RVR_BATTLE_LOBBYINFO_REQUEST,new GsGetGuildRvRBattleLobbyInfoRequest());Check(!lobby.ableToJoin&&lobby.guildRankInfo.Count==0,"No fabricated online guild-war matchmaking");
            await Bad(b,GameServerCommand.GS_GUILD_RVR_BATTLE_START_REQUEST,new GsGuildRvRBattleStartRequest{spotId=1,waveId=1},"Online war unavailable without consuming attempts");
            var temporary=new List<LocalPlayer>();
            for(int i=0;i<35;i++){var member=r.Store.GetOrCreate("roster-"+i+new string('z',100));member.Nickname=new string('甲',16);member.Level=17;member.Guild.GuildId=g.Id;g.Members[member.Uid]=new(){Joined=ProtocolRouter.Now};temporary.Add(member);}
            var many=await a.Request<GsGetMyGuildUserInfoListOk>(GameServerCommand.GS_GET_MY_GUILD_USER_INFO_LIST_REQUEST,new GsGetMyGuildUserInfoListRequest());Check(many.dataCount==37&&many.guildUserInfoList.Count==37,"Native multi-part roster spans three encrypted frames without hanging");
            foreach(var member in temporary){g.Members.Remove(member.Uid);member.Guild.GuildId=0;}
            var oldChat=g.Chat.ToList();uint oldSequence=g.ChatSequence;
            for(int i=0;i<30;i++)await a.Request<GsSendGuildChatMessageOk>(GameServerCommand.GS_SEND_GUILD_CHAT_MESSAGE_REQUEST,new GsSendGuildChatMessageRequest{msg=i+new string('聊',195)});
            var longChat=await b.Request<GsFetchGuildChatMessageOk>(GameServerCommand.GS_FETCH_GUILD_CHAT_MESSAGE_REQUEST,new GsFetchGuildChatMessageRequest());Check(longChat.msgList.Count>0&&longChat.msgList.Count<30&&b.Last.Length<8192,"Long guild-chat history stays inside original encrypted frame limit");g.Chat=oldChat;g.ChatSequence=oldSequence;
            p.Guild.Day--;q.Guild.Day--;var daily=await a.Request<GsGetGuildContributionInfoOk>(GameServerCommand.GS_GET_GUILD_CONTRIBUTION_INFO_REQUEST,new GsGetGuildContributionInfoRequest());Check(daily.currentSlotId==0&&daily.resetCount==0&&q.Guild.WorshipCount==0&&g.Level==2&&g.DonationCount==10,"04:00 resets daily actions while keeping guild progression");uint previousTokens=p.GuildTokens;p.GuildTokens=uint.MaxValue;
            await Bad(a,GameServerCommand.GS_GUILD_CONTRIBUTION_REQUEST,new GsGuildContributionRequest{itemInfo=new(){itemID=ticket,itemCount=1}},"Token overflow during donation");p.GuildTokens=previousTokens;r.Store.Save();
        }
        var workspace=new SaveWorkspace(Path.Combine(path,"workspace"));
        string original=JsonSerializer.SerializeToElement(workspace.Command("list",legacy:path)).GetProperty("activeProfileId").GetString()!;
        string archive=JsonSerializer.SerializeToElement(workspace.Command("backup")).GetProperty("path").GetString()!;
        workspace.Command("import",file:archive,name:"门派存档导入");
        var profiles=JsonSerializer.SerializeToElement(workspace.Command("list")).GetProperty("profiles");string imported=profiles.EnumerateArray().Select(x=>x.GetProperty("Id").GetString()!).Single(x=>x!=original);
        Check(File.ReadAllBytes(Path.Combine(workspace.Saves(original),"players.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(workspace.Saves(imported),"players.json"))),"Save manager exports/imports exact guild membership, progression and receipts");
        workspace.Command("new",name:"全新门派存档");string fresh=JsonSerializer.SerializeToElement(workspace.Command("list")).GetProperty("activeProfileId").GetString()!;
        var newStore=new PlayerStore(workspace.Saves(fresh),new GameCatalog(root));var newPlayer=newStore.GetOrCreate("local-player");Check(newPlayer.Guild.GuildId==0&&newPlayer.FoundedGuilds.Count==0&&newPlayer.GuildTokens==0,"New save starts with no inherited guild or currency");
        Check(new PlayerStore(workspace.Saves(original),new GameCatalog(root)).GetOrCreate("local-player").FoundedGuilds.Single().Level==2,"Creating a new save preserves the old guild");
        Check(ProtocolRouter.GuildAttendanceDay(20000ul*86400+14*3600+119)+1==ProtocolRouter.GuildAttendanceDay(20000ul*86400+14*3600+120),"Attendance settlement crosses exactly at China 22:02");
        await using(var r=new LocalRuntime(root,path,0,0,0,false)){
            using var a=new Peer(root);await a.Login(r);var p=r.Store.Find(a.Key.uid)!;var g=p.FoundedGuilds.Single();var mine=await a.Request<GsGetMyGuildInfoOk>(GameServerCommand.GS_GET_MY_GUILD_INFO_REQUEST,new GsGetMyGuildInfoRequest());
            Check(mine.guildInfo.guildID==guildId&&mine.guildInfo.guildLevel==2&&mine.grade==GuildUserGrade.GUILD_USER_GRADE_NORMAL&&mine.guildInfo.guildName=="青龙堂","Restart retains founding record after leadership transfer");
            Check(g.Members.Count==2&&g.Chat.Count==1&&g.ClaimedMissions.Contains(7200)&&p.GuildTokens>0,"Restart retains roster, chat, rewards and currency");
            using var b=new Peer(root);await b.Login(r,"local-friend");await a.Request<object>(GameServerCommand.GS_WITHDRAW_GUILD_REQUEST,new GsWithDrawGuildRequest());await b.Request<GsDisbandGuildOk>(GameServerCommand.GS_DISBAND_GUILD_REQUEST,new GsDisbandGuildRequest());Check(g.Disbanded&&g.Members.Count==0,"Last leader can disband without losing prior save");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/guild-protocol-latest.json",JsonSerializer.Serialize(new{passed=true,count=checks.Count,productionSaveUsed=false,checks,serverCoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Guild protocol {checks.Count}/{checks.Count} passed");
    }
}
