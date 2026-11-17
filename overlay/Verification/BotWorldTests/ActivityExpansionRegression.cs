using System.Text.Json;
using ProtoBuf;

internal static class ActivityExpansionRegression
{
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);
        if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated activity fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();ProtocolRouter.RefreshActivityDay(p.Activities,ProtocolRouter.Now);p.Activities.Daily["MCT_CLEAR_PVE"]=5;runtime.Store.Save();}
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","activity-expansion-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool pass,string message){if(!pass)throw new InvalidDataException(message);checks.Add(message);Console.WriteLine("PASS "+message);}
        var catalog=new ActivityCatalog(root);Check(catalog.Events.Count==11&&catalog.Events.Sum(e=>e.Goals.Count)==36,"Eleven playable activities with 36 reward goals");
        var legacy=new LocalActivityState{Day=AttendanceRules.DayIndex(ProtocolRouter.Now),Week=(AttendanceRules.DayIndex(ProtocolRouter.Now)+3)/7};legacy.PermanentClaims.Add("900005/1");
        ProtocolRouter.RefreshActivityDay(legacy,ProtocolRouter.Now);Check(legacy.Total["LOGIN"]==1&&legacy.PermanentClaims.Contains("900005/1"),"Legacy saves start today's lifetime attendance without losing growth claims");
        ProtocolRouter.RefreshActivityDay(legacy,ProtocolRouter.Now);Check(legacy.Total["LOGIN"]==1,"Repeated reads do not add lifetime login days");
        ProtocolRouter.RefreshActivityDay(legacy,ProtocolRouter.Now+3*86400);Check(legacy.Total["LOGIN"]==2,"Missed days are not invented and consecutive login is not required");
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Activities,p.ActivityReceipts,p.Gold,p.CashPoint,p.Inventory,p.EquipmentItems});
            GsReceivePromotionRewardRequest Claim(uint e,uint g)=>new(){eventId=e,dataId=g,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION};
            async Task<GsGetPromotionUserInfoOk> Info()=>await peer.Request<GsGetPromotionUserInfoOk>(GameServerCommand.GS_GET_PROMOTION_USER_INFO_REQUEST,new GsGetPromotionUserInfoRequest());
            var defs=await peer.Request<GsGetDebugPromotionInfoOk>(GameServerCommand.GS_GET_DEBUG_PROMOTION_DATA_REQUEST,new GsGetDebugPromotionInfoRequest());
            Check(defs.totalCount==11&&defs.currentCount==11&&defs.promotionDataList.Select(e=>e.Id).Distinct().Count()==11,"Native multipart definitions reassemble all eleven activities without truncation");
            var parts=LocalRuntime.SerializeReplyParts(defs).ToArray();Check(parts.All(b=>b.Length+WireCodec.HeaderSize<=WireCodec.MaxPacketSize),"Every activity frame fits the legacy 8 KiB limit");
            Check(LocalRuntime.SerializeReplyParts(await Info()).Single().Length+WireCodec.HeaderSize<=WireCodec.MaxPacketSize,"All 36 progress rows fit one native progress reply");
            foreach(var e in catalog.Events.Where(e=>e.Id>=900006))foreach(var g in e.Goals) {
                Check(g.Rewards.Where(r=>r.Type=="ITEM").All(r=>runtime.Catalog.ItemsById.ContainsKey(r.Id)),"Original reward IDs exist: "+e.Id+"/"+g.Id);
                var shown=defs.promotionDataList.Single(x=>x.Id==e.Id).PromotionInfoList.Single(x=>x.DataId==g.Id);
                Check(shown.Quantity.SequenceEqual(g.Rewards.Select(r=>r.Count))&&shown.ItemId.SequenceEqual(g.Rewards.Select(r=>r.Id)),"Native reward thumbnails agree with original data: "+e.Id+"/"+g.Id);
            }
            string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900006,2),success:false);Check(State()==before,"Tomorrow's login gift is rejected atomically");
            before=State();await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900007,1),success:false);Check(State()==before,"Unplayed PvP cannot claim an activity gift");
            await peer.Request<GsCreateGuildOk>(GameServerCommand.GS_CREATE_GUILD_REQUEST,new GsCreateGuildRequest{guildName="活动验证门派",joinType=GuildJoinType.GUILD_JOIN_TYPE_OPEN,joinMinLevel=17});
            var attend=new GsCheckGuildAttendanceRequest();await peer.Request<GsCheckGuildAttendanceOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,attend);
            Check(p.Activities.Weekly["MCT_LOGIN_GUILD_ATTENDANCE"]==1,"A real guild check-in advances its activity");
            before=State();await peer.Request<GsCheckGuildAttendanceOk>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,attend,retry:true);Check(State()==before,"Check-in retries cannot add progress");
            await peer.Request<FailPacket>(GameServerCommand.GS_CHECK_GUILD_ATTENDANCE_REQUEST,new GsCheckGuildAttendanceRequest(),success:false);Check(State()==before,"Repeated fresh check-in cannot add progress");
            Check((await Info()).promotionRewardInfo.Any(r=>r.eventId==900010&&r.dataId==1&&r.status==0),"Native refresh exposes the earned guild gift");
            int oldCount=p.Inventory.GetValueOrDefault(205060321u);p.Inventory[205060321]=int.MaxValue;p.Activities.Weekly["MCT_ENTER_RANKING_PVP"]=1;
            before=State();await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(900007,1),success:false);Check(State()==before,"A restored material reward at inventory capacity cannot partially grant or mark claimed");p.Inventory[205060321]=oldCount;
            foreach(var e in catalog.Events.Where(e=>e.Id>=900006))foreach(var g in e.Goals) {
                // Exercise every original grant and receipt after setting the exact
                // server ledger. Actual PvP settlement is covered by competitive tests.
                (e.Period=="once"?p.Activities.Total:p.Activities.Weekly)[g.Counter]=(uint)g.Target;
                uint gold=p.Gold,cash=p.CashPoint;var inventory=new Dictionary<uint,int>(p.Inventory);
                var req=Claim(e.Id,g.Id);await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,req);
                Check(p.Gold==gold+g.Rewards.Where(r=>r.Type=="GOLD").Sum(r=>r.Count)&&p.CashPoint==cash+g.Rewards.Where(r=>r.Type=="CASH").Sum(r=>r.Count)&&g.Rewards.Where(r=>r.Type=="ITEM").All(r=>p.Inventory[(uint)r.Id]==inventory.GetValueOrDefault((uint)r.Id)+r.Count),"Exact original currencies and materials granted: "+e.Id+"/"+g.Id);
                before=State();var bytes=peer.LastPayload.ToArray();await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,req,retry:true);Check(State()==before&&bytes.SequenceEqual(peer.LastPayload),"Retry is receipt-safe: "+e.Id+"/"+g.Id);
                await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,Claim(e.Id,g.Id),success:false);Check(State()==before,"Fresh duplicate claim is rejected: "+e.Id+"/"+g.Id);
            }
            var info=await Info();Check(info.promotionRewardInfo.Count(x=>x.eventId>=900006&&x.status==1)==22,"All restored gifts refresh to native claimed state");
            p.Level=1;defs=await peer.Request<GsGetDebugPromotionInfoOk>(GameServerCommand.GS_GET_DEBUG_PROMOTION_DATA_REQUEST,new GsGetDebugPromotionInfoRequest());Check(defs.promotionDataList.All(e=>e.Id is not (900007 or 900008 or 900009 or 900010)),"Low-level characters cannot see locked mode activities");p.Level=99;
            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var p=runtime.Store.All().Single();Check(p.Activities.PermanentClaims.Count(x=>x.StartsWith("900006/"))==7&&p.Activities.WeeklyClaims.Count==12,"Restored login and weekly claims survive server restart");
            var sunday=(ulong)new DateTimeOffset(2026,11,8,23,59,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
            ProtocolRouter.RefreshActivityDay(p.Activities,sunday);p.Activities.WeeklyClaims.Add("900007/1");p.Activities.Weekly["MCT_ENTER_RANKING_PVP"]=7;
            ProtocolRouter.RefreshActivityDay(p.Activities,sunday+120);Check(p.Activities.WeeklyClaims.Count==0&&p.Activities.Weekly.GetValueOrDefault("MCT_ENTER_RANKING_PVP")==0&&p.Activities.PermanentClaims.Count(x=>x.StartsWith("900006/"))==7,"Monday resets weekly objectives while keeping one-time login claims");
        }
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/activity-expansion-protocol-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=Hash(typeof(LocalRuntime).Assembly.Location),activityConfigSha256=Hash(Path.Combine(root,"LocalServer/data/activities.json"))},new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Audit(string root)
    {
        var tables = new Dictionary<string,object>();
        tables["PvpLevels"]=new CompetitiveCatalog(root).Modes.Select(x=>new{x.PvpBattleType,x.RequirePCLevel});
        foreach (string name in new[]{"PromotionLaunchingDataTable","PromotionLongtermDataTable"}) {
            using var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));
            var table=Serializer.Deserialize<BscTable.PromotionDataTable>(input);
            tables[name]=table.List.Select(x=>new{x.Id,x.PromotionType,x.PromotionStyle,x.PromotionNameAlias,x.DescAlias,x.StartDate,x.EndDate,x.OpenPcLevel,goals=x.PromotionInfoList.Select(g=>new{g.DataId,g.MissionId,g.MissionName,g.MissionDes,g.NeedCount,g.Value,g.ItemIdType,g.ItemId,g.Quantity})}).ToArray();
        }
        using(var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables/EventMissionDataTable.pb"))) {
            tables["EventMissionDataTable"]=Serializer.Deserialize<BscTable.MissionDataTable>(input).List.Select(x=>new{x.MissionId,Counter=x.CheckType.ToString(),x.MissionName,x.MissionDes,x.Check1Id,x.Check2Id,x.NeedCount,x.RewardIdType,x.RewardId,x.RewardQuantity}).ToArray();
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/activity-original-catalog.json",JsonSerializer.Serialize(tables,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
