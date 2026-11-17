using System.Globalization;
using System.Text.Json;

internal sealed class LocalActivityState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public ulong Week {get;set;}
    public Dictionary<string,uint> Daily {get;set;}=[];
    public Dictionary<string,uint> Weekly {get;set;}=[];
    public Dictionary<string,uint> Total {get;set;}=[];
    public HashSet<string> DailyClaims {get;set;}=[];
    public HashSet<string> WeeklyClaims {get;set;}=[];
    public HashSet<string> PermanentClaims {get;set;}=[];
}

internal sealed class ActivityCatalog
{
    internal sealed class Reward
    {
        public string Type {get;set;}="";
        public int Id {get;set;}
        public int Count {get;set;}
    }
    internal sealed class Goal
    {
        public uint Id {get;set;}
        public string Name {get;set;}="";
        public string Counter {get;set;}="";
        public int Target {get;set;}
        public int SourceMission {get;set;}
        public List<Reward> Rewards {get;set;}=[];
    }
    internal sealed class Event
    {
        public uint Id {get;set;}
        public string Name {get;set;}="";
        public string Description {get;set;}="";
        public string Period {get;set;}="daily";
        public int Level {get;set;}=1;
        public int LoginPromotion {get;set;}
        public List<Goal> Goals {get;set;}=[];
    }
    public readonly List<Event> Events;
    public ActivityCatalog(string root)
    {
        Events=JsonSerializer.Deserialize<List<Event>>(File.ReadAllText(Path.Combine(root,"LocalServer/data/activities.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
        T Read<T>(string name){using var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return ProtoBuf.Serializer.Deserialize<T>(input);}
        var missions=Read<BscTable.MissionDataTable>("EventMissionDataTable").List.ToDictionary(x=>x.MissionId);
        var promotions=Read<BscTable.PromotionDataTable>("PromotionLaunchingDataTable").List;
        List<Reward> Rewards(IReadOnlyList<BscTable.REWARD_TYPE> types,IReadOnlyList<int> ids,IReadOnlyList<int> counts) {
            if(types.Count!=ids.Count||types.Count!=counts.Count)throw new InvalidDataException("Mismatched original activity rewards");
            return types.Select((type,i)=>new Reward{Type=type switch{BscTable.REWARD_TYPE.REWARD_TYPE_GOLD=>"GOLD",BscTable.REWARD_TYPE.REWARD_TYPE_CASH=>"CASH",BscTable.REWARD_TYPE.REWARD_TYPE_ITEM=>"ITEM",_=>throw new InvalidDataException("Unsupported original activity reward: "+type)},Id=ids[i],Count=counts[i]}).ToList();
        }
        foreach(var e in Events) {
            if(e.LoginPromotion!=0) {
                var row=promotions.Single(x=>x.Id==e.LoginPromotion&&x.PromotionType==BscTable.PROMOTION_TYPE.PROMOTION_TYPE_LOGIN);
                if(e.Period!="once"||e.Goals.Count!=0)throw new InvalidDataException("Login recovery must be a one-time original series");
                e.Goals.AddRange(row.PromotionInfoList.Select(g=>new Goal{Id=(uint)g.DataId,Name="累计登录 {0} 天",Counter="LOGIN",Target=g.Value,Rewards=Rewards(g.ItemIdType,g.ItemId,g.Quantity)}));
            }
            foreach(var g in e.Goals.Where(g=>g.SourceMission!=0)) {
                var row=missions[g.SourceMission];
                if(row.Check1Id!=0||row.Check2Id!=0)throw new InvalidDataException("Activity mission requires an unsupported filter");
                g.Counter=row.CheckType.ToString();g.Target=row.NeedCount;g.Rewards=Rewards(row.RewardIdType,row.RewardId,row.RewardQuantity);
            }
        }
        string[] counters=["LOGIN","LEVEL","MCT_CLEAR_PVE","MCT_CLEAR_RESOURCE_DUNGEON","MCT_CAST_BATTLE_BOARD_DICE","MCT_WIN_CHALLENGE_STAGE","MCT_ENTER_RANKING_PVP","MCT_ENTER_ARENA_PVP","MCT_ENTER_BALANCE_PVP","MCT_LOGIN_GUILD_ATTENDANCE"];
        if(Events.Count==0||Events.Select(x=>x.Id).Distinct().Count()!=Events.Count||Events.Any(e=>e.Id<900000||e.Id>int.MaxValue||string.IsNullOrWhiteSpace(e.Name)||e.Level<1||e.Period is not ("daily" or "weekly" or "once")||e.Goals.Count==0||e.Goals.Select(g=>g.Id).Distinct().Count()!=e.Goals.Count||e.Goals.Any(g=>g.Id==0||g.Id>int.MaxValue||g.Target<=0||e.Period=="once"&&g.Counter is not ("LEVEL" or "LOGIN" or "MCT_CLEAR_PVE")||!counters.Contains(g.Counter)||g.Rewards.Count is <1 or >3||g.Rewards.Any(r=>r.Count<=0||r.Type is not ("GOLD" or "CASH" or "ITEM")||r.Type=="ITEM"&&r.Id<=0))))
            throw new InvalidDataException("Invalid local activity definitions");
    }
}

internal sealed partial class ProtocolRouter
{
    private ActivityCatalog? activityCatalog;
    private ActivityCatalog Activities=>activityCatalog??=new(RootDirectory);
    internal static void RefreshActivityDay(LocalActivityState state,ulong now)
    {
        ulong day=AttendanceRules.DayIndex(now),week=(day+3)/7; // 1970-01-01 was Thursday; weeks start Monday.
        if(state.Week!=week){state.Week=week;state.Weekly.Clear();state.WeeklyClaims.Clear();}
        if(state.Day!=day){state.Day=day;state.Daily.Clear();state.DailyClaims.Clear();state.Daily["LOGIN"]=1;state.Weekly["LOGIN"]=state.Weekly.GetValueOrDefault("LOGIN")+1;state.Total["LOGIN"]=(uint)Math.Min(uint.MaxValue,(ulong)state.Total.GetValueOrDefault("LOGIN")+1);}
        // Existing saves have no lifetime ledger. Begin at today's visit, without
        // manufacturing missed attendance days or resetting already claimed gifts.
        if(!state.Total.ContainsKey("LOGIN"))state.Total["LOGIN"]=1;
    }
    private void RefreshActivities(LocalPlayer p)
    {
        ulong day=p.Activities.Day;bool migrate=!p.Activities.Total.ContainsKey("LOGIN");RefreshActivityDay(p.Activities,Now);if(day!=p.Activities.Day||migrate)store.Save();
    }
    private void AddActivityProgress(LocalPlayer p,string counter,uint amount)
    {
        foreach(var counts in new[]{p.Activities.Daily,p.Activities.Weekly,p.Activities.Total})counts[counter]=(uint)Math.Min(uint.MaxValue,(ulong)counts.GetValueOrDefault(counter)+amount);
    }
    private static HashSet<string> ActivityClaims(LocalPlayer p,ActivityCatalog.Event e)=>e.Period switch{"daily"=>p.Activities.DailyClaims,"weekly"=>p.Activities.WeeklyClaims,_=>p.Activities.PermanentClaims};
    private static string ActivityKey(ActivityCatalog.Event e,ActivityCatalog.Goal g)=>$"{e.Id}/{g.Id}";
    private static uint ActivityProgress(LocalPlayer p,ActivityCatalog.Event e,ActivityCatalog.Goal g)=>Math.Min((uint)g.Target,g.Counter=="LEVEL"?p.Level:(e.Period=="daily"?p.Activities.Daily:e.Period=="weekly"?p.Activities.Weekly:p.Activities.Total).GetValueOrDefault(g.Counter));
    private bool ActivityHasRewards(LocalPlayer p)=>Activities.Events.Any(e=>p.Level>=e.Level&&e.Goals.Any(g=>ActivityProgress(p,e,g)>=g.Target&&!ActivityClaims(p,e).Contains(ActivityKey(e,g))));
    private static (DateTimeOffset Start,DateTimeOffset End) ActivityPeriod(ActivityCatalog.Event e,ulong now)
    {
        var local=DateTimeOffset.FromUnixTimeSeconds((long)now).ToOffset(TimeSpan.FromHours(8));
        var start=new DateTimeOffset(local.Year,local.Month,local.Day,0,0,0,local.Offset);
        return e.Period switch{"daily"=>(start,start.AddDays(1)),"weekly"=>(start.AddDays(-((int)local.DayOfWeek+6)%7),start.AddDays(7-((int)local.DayOfWeek+6)%7)),_=>(new(2000,1,1,0,0,0,local.Offset),new(2099,12,31,23,59,59,local.Offset))};
    }
    private GsGetPromotionUserInfoOk ActivityInfo(LocalPlayer p)
    {
        var result=new GsGetPromotionUserInfoOk{debugEnable=true};
        foreach(var e in Activities.Events.Where(e=>p.Level>=e.Level)){
            var period=ActivityPeriod(e,Now);
            foreach(var g in e.Goals){
                uint progress=ActivityProgress(p,e,g);result.promotionInfo.Add(new(){eventId=e.Id,dataId=g.Id,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION,value=progress,startTime=(ulong)period.Start.ToUnixTimeSeconds(),endTime=(ulong)period.End.ToUnixTimeSeconds()});
                bool claimed=ActivityClaims(p,e).Contains(ActivityKey(e,g));
                if(claimed||progress>=g.Target)result.promotionRewardInfo.Add(new(){eventId=e.Id,dataId=g.Id,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION,status=claimed?1u:0u});
            }
        }
        return result;
    }
    private GsGetDebugPromotionInfoOk ActivityDefinitions(LocalPlayer p)
    {
        var result=new GsGetDebugPromotionInfoOk();int priority=0;
        foreach(var e in Activities.Events.Where(e=>p.Level>=e.Level)){
            var period=ActivityPeriod(e,Now);
            string Date(DateTimeOffset value)=>value.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
            var row=new BscTable.PromotionData{Id=(int)e.Id,PromotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION,PromotionStyle=BscTable.PROMOTION_STYLE.PROMOTION_STYLE_CONTENTS_BANNER_MISSION,PromotionNameAlias=e.Name,SubTitleAlias=e.Name,DescAlias=e.Description,OpenPcLevel=e.Level,PrePromotionDate=Date(period.Start),StartDate=Date(period.Start),EndDate=Date(period.End),DailyReset=e.Period=="daily",ViewPriority=priority++};
            foreach(var g in e.Goals){
                var info=new BscTable.PromotionInfo{DataId=(int)g.Id,MissionId=(int)g.Id,MissionName=g.Name,MissionDes=g.Name,RewardNameAlias=g.Name,MissionType=BscTable.MISSION_TYPE.EVENT_MISSION,NeedCount=g.Target,Value=g.Target};
                foreach(var reward in g.Rewards){info.ItemIdType.Add(AttendanceRules.RewardType(reward.Type));info.ItemId.Add(reward.Id);info.Quantity.Add(reward.Count);}
                row.PromotionInfoList.Add(info);
            }
            result.promotionDataList.Add(row);
        }
        result.totalCount=result.currentCount=(uint)result.promotionDataList.Count;return result;
    }
    private Reply? HandleActivities(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_GET_PROMOTION_USER_INFO_REQUEST")return ok(ActivityInfo(p));
        if(name=="GS_GET_DEBUG_PROMOTION_DATA_REQUEST")return ok(ActivityDefinitions(p));
        if(request is not GsReceivePromotionRewardRequest claim)return null;
        return FeatureReceipt(p,p.ActivityReceipts,name,session,payload,()=>{
            var e=Activities.Events.FirstOrDefault(e=>e.Id==claim.eventId);var goal=e?.Goals.FirstOrDefault(g=>g.Id==claim.dataId);
            if(e==null||goal==null||p.Level<e.Level||claim.promotionType!=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION)return fail("invalid local activity reward",ServerErrorCode.ERROR_INVALID_ID);
            var claims=ActivityClaims(p,e);string key=ActivityKey(e,goal);
            if(claims.Contains(key)||ActivityProgress(p,e,goal)<goal.Target)return fail("activity goal is incomplete or reward already claimed",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var grant=new ShopGrant();foreach(var reward in goal.Rewards)if(!AddShopReward(p,grant,AttendanceRules.RewardType(reward.Type),reward.Id,reward.Count))return fail("activity reward is unavailable",ServerErrorCode.ERROR_INVALID_ID);
            if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("activity reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsReceivePromotionRewardOk{earnedCoinInfo=GrantCoins(grant)};
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);claims.Add(key);result.coinInfo=Coins(p);return ok(result);
        },fail);
    }
}
