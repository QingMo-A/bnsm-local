extern alias game;
using ProtoBuf;
using System.Text.Json;
using M = game::BscTable.MISSION_TYPE;
using C = game::BscTable.CHECK_TYPE;

internal sealed class LocalMissionState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public uint DailyLevel {get;set;}
    public ulong RegisteredAt {get;set;}
    public uint DicePoints {get;set;}
    public Dictionary<uint,ulong> Claimed {get;set;}=[];
    public Dictionary<uint,ulong> DailyClaimed {get;set;}=[];
    public Dictionary<C,uint> Counters {get;set;}=[];
    public Dictionary<C,uint> DailyCounters {get;set;}=[];
    public HashSet<uint> EnteredDeckMissions {get;set;}=[];
}

internal sealed class MissionCatalog
{
    public readonly Dictionary<uint,BscTable.MissionData> Rows;
    public readonly Dictionary<int,BscTable.StageTargetDeck> Decks;
    public MissionCatalog(string root)
    {
        T Read<T>(string name) {using var f=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",name+".pb"));return Serializer.Deserialize<T>(f);}
        Rows=Read<BscTable.MissionDataTable>("MissionTable").List.ToDictionary(x=>(uint)x.MissionId);
        Decks=Read<BscTable.StageTargetDeckTable>("StageTargetDeckTable").List.ToDictionary(x=>x.DeckId);
        if(Rows.Values.Any(x=>x.NeedCount<1||x.OpenConditionType.Count!=x.OpenConditionValue.Count||x.RewardId.Count!=x.RewardIdType.Count||x.RewardId.Count!=x.RewardQuantity.Count))
            throw new InvalidDataException("Invalid original mission table");
    }
}

internal sealed partial class ProtocolRouter
{
    private MissionCatalog? missionCatalog;
    private MissionCatalog Missions=>missionCatalog??=new(RootDirectory);
    private static bool Daily(BscTable.MissionData row)=>row.MissionType==M.DAILY_MISSION;
    internal static void RefreshMissionDay(LocalMissionState state,uint level,ulong now)
    {
        ulong day=(now+8*3600)/86400; // GsGetUserGameInfoOk.serverResetHour is 0: UTC+8 midnight.
        if(state.RegisteredAt==0)state.RegisteredAt=now;
        if(state.Day==day)return;
        state.Day=day;state.DailyLevel=level;state.DailyCounters.Clear();state.DailyClaimed.Clear();
    }
    private void RefreshMissions(LocalPlayer p)
    {
        ulong old=p.Missions.Day;RefreshMissionDay(p.Missions,p.Level,Now);
        if(old!=p.Missions.Day)store.Save();
    }
    private bool MissionOpen(LocalPlayer p,BscTable.MissionData row)
    {
        uint level=Daily(row)?p.Missions.DailyLevel:p.Level;
        for(int i=0;i<row.OpenConditionType.Count;i++) {
            int v=row.OpenConditionValue[i];
            bool open=(int)row.OpenConditionType[i] switch {
                1=>level>=v,2=>level<=v,3=>p.ClearStage>=v,
                4=>p.Missions.Claimed.ContainsKey((uint)v)||p.Missions.DailyClaimed.ContainsKey((uint)v),
                5=>catalog.VipFor(p.VipExp).VipLv>=v,6=>catalog.VipFor(p.VipExp).VipLv<=v,
                7=>MyGuild(p)?.Level>=v,8=>MyGuild(p)?.Level<=v,
                10=>true, // All shipped 1.97 content versions are present, not the retired live service date.
                _=>false
            };
            if(!open)return false;
        }
        if(row.IconFlag=="HUDICONS_GUILD"&&MyGuild(p)==null)return false;
        return true;
    }
    internal static bool MissionTimeOpen(BscTable.MissionData row,ulong now,bool checking)
    {
        var time=DateTimeOffset.FromUnixTimeSeconds((long)now).ToOffset(TimeSpan.FromHours(8));
        int clock=time.Hour*10000+time.Minute*100+time.Second;
        int start=checking?row.CheckStartTime:row.CheckOpenTime,end=checking?row.CheckEndTime:row.CheckCloseTime;
        return start==0&&end==0||clock>=start&&clock<=end;
    }
    private uint MissionCount(LocalPlayer p,BscTable.MissionData row)
    {
        uint Stage(bool hard,bool legend,bool dungeon,bool perfect) {
            var difficulty=legend?BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND:hard?BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD:BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL;
            var record=(dungeon?DungeonRecordsOf(p,difficulty):StageRecordsOf(p,difficulty)).GetValueOrDefault((uint)row.Check1Id);
            if(perfect)return record?.Stars==3&&record.PerfectTime>0&&record.PerfectTime<=row.Check2Id?1u:0;
            return record?.Stars>0||!dungeon&&StageUnlock.ClearStageOf(p,difficulty)>=row.Check1Id?1u:0;
        }
        return row.CheckType switch {
            C.MCT_CLEAR_TARGET_STAGE_NORMAL=>Stage(false,false,false,false),
            C.MCT_CLEAR_TARGET_STAGE_HARD=>Stage(true,false,false,false),
            C.MCT_CLEAR_TARGET_STAGE_LEGEND=>Stage(false,true,false,false),
            C.MCT_CLEAR_TARGET_DUNGEON_HARD=>Stage(true,false,true,false),
            C.MCT_PERFECT_CLEAR_STAGE_IN_TIME_HARD=>Stage(true,false,false,true),
            C.MCT_PERFECT_CLEAR_STAGE_IN_TIME_LEGEND=>Stage(false,true,false,true),
            C.MCT_PERFECT_CLEAR_DUNGEON_IN_TIME_HARD=>Stage(true,false,true,true),
            C.MCT_COLLECT_CARD_COUNT=>(uint)catalog.Cards.Where(x=>p.CollectedCardIds.Contains((uint)x.Id)||p.CardIds.Contains((uint)x.Id)).Select(x=>x.EvolveFirstAncestor).Distinct().Count(),
            C.MCT_GET_CARD_BY_RANK=>(uint)catalog.Cards.Where(x=>x.Rank==row.Check1Id&&(p.CollectedCardIds.Contains((uint)x.Id)||p.CardIds.Contains((uint)x.Id))).Select(x=>x.EvolveFirstAncestor).Distinct().Count(),
            C.MCT_FORMATION_LEVEL_UP=>(uint)p.FormationLevels.Values.Sum(x=>Math.Max(0,x-1)),
            C.MCT_ENTER_WITH_DECK_AT_NORMAL_STAGE or C.MCT_ENTER_WITH_DECK_AT_HARD_STAGE=>p.Missions.EnteredDeckMissions.Contains((uint)row.MissionId)?1u:0,
            C.MCT_VISIT_MY_HOME=>MissionTimeOpen(row,Now,true)?1u:0,
            _=>(Daily(row)?p.Missions.DailyCounters:p.Missions.Counters).GetValueOrDefault(row.CheckType)
        };
    }
    private List<PKProgressingMissionData> MissionList(LocalPlayer p)=>Missions.Rows.Values
        .Where(row=>!(Daily(row)?p.Missions.DailyClaimed:p.Missions.Claimed).ContainsKey((uint)row.MissionId)&&MissionOpen(p,row)&&MissionTimeOpen(row,Now,false))
        .Select(row=>{uint count=Math.Min((uint)row.NeedCount,MissionCount(p,row));return new PKProgressingMissionData {missionId=(uint)row.MissionId,count=count,registTime=Daily(row)?p.Missions.Day*86400-8*3600:p.Missions.RegisteredAt,status=count>=row.NeedCount?MissionStatus.MISSION_STATUS_COMPLETED:MissionStatus.MISSION_STATUS_PROGRESSING};}).ToList();
    private static string MissionVersion(PKProgressingMissionData row)=>$"{row.count}/{row.status}/{row.registTime}";
    private static void RememberMissions(ConnectionState connection,List<PKProgressingMissionData> list)
    {
        connection.MissionVersions.Clear();foreach(var row in list)connection.MissionVersions[row.missionId]=MissionVersion(row);
    }
    private Reply? HandleMissions(string name,object? request,LocalPlayer p,ConnectionState connection,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_GET_PROGRESSING_MISSION_LIST_REQUEST") {
            var list=MissionList(p);RememberMissions(connection,list);var r=new GsGetProgressingMissionListOk{missionInfo=new()};r.list.AddRange(list);return ok(r);
        }
        if(name=="GS_GET_CHANGED_MISSION_LIST_REQUEST") {
            var list=MissionList(p);var r=new GsGetChangedMissionListOk{missionInfo=new(),hasNewEventRewardFlag=ActivityHasRewards(p)};
            r.list.AddRange(list.Where(x=>connection.MissionVersions.GetValueOrDefault(x.missionId)!=MissionVersion(x)));
            var ids=list.Select(x=>x.missionId).ToHashSet();r.deletedDailyMissionIdList.AddRange(connection.MissionVersions.Keys.Where(x=>!ids.Contains(x)));
            RememberMissions(connection,list);return ok(r);
        }
        if(name=="GS_GET_REWARDED_MISSION_LIST_REQUEST") {
            var r=new GsGetRewardedMissionListOk();r.list.AddRange(p.Missions.Claimed.Concat(p.Missions.DailyClaimed).OrderBy(x=>x.Key).Select(x=>new PKRewardedMissionData{missionId=x.Key,rewardedTime=x.Value}));return ok(r);
        }
        if(request is not GsMissionRewardRequest claim)return null;
        return FeatureReceipt(p,p.MissionReceipts,name,session,payload,()=>{
            if(!Missions.Rows.TryGetValue(claim.missionId,out var row)||!MissionOpen(p,row)||!MissionTimeOpen(row,Now,false)||
                (Daily(row)?p.Missions.DailyClaimed:p.Missions.Claimed).ContainsKey(claim.missionId)||MissionCount(p,row)<row.NeedCount)
                return fail("mission is locked, incomplete or already rewarded",ServerErrorCode.ERROR_INVALID_ID);
            var grant=new ShopGrant();ulong exp=0;uint title=0;
            // BscCommonRewardPopup.MakeRewardList displays the first two rewards
            // plus the dedicated dice-point reward in its third display slot.
            int rewardCount=row.RewardDicePoint>0?Math.Min(2,row.RewardId.Count):row.RewardId.Count;
            uint dice=row.RewardDicePoint>0&&FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_BATTLE_BOARD)?(uint)row.RewardDicePoint:0;
            for(int i=0;i<rewardCount;i++) {
                if(row.RewardQuantity[i]<=0)return fail("invalid original mission reward",ServerErrorCode.ERROR_INVALID_ID);
                if(row.RewardIdType[i]==BscTable.REWARD_TYPE.REWARD_TYPE_TITLE) {
                    if(title!=0||row.RewardQuantity[i]!=1||!catalog.Titles.Any(t=>t.Id==row.RewardId[i]))return fail("invalid original mission title",ServerErrorCode.ERROR_INVALID_ID);
                    title=(uint)row.RewardId[i];
                }
                else if(row.RewardIdType[i]==BscTable.REWARD_TYPE.REWARD_TYPE_PC_EXP)exp+=(uint)row.RewardQuantity[i];
                else if(!AddShopReward(p,grant,row.RewardIdType[i],row.RewardId[i],row.RewardQuantity[i]))return fail("unavailable original mission reward",ServerErrorCode.ERROR_INVALID_ID);
            }
            var growth=StageGrowth(p,exp,0);
            if(growth==null||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0)||(ulong)growth.Value.Stamina+grant.Stamina>uint.MaxValue||(ulong)p.Missions.DicePoints+dice>uint.MaxValue)
                return fail("mission reward storage capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var r=new GsMissionRewardOk{missionId=claim.missionId,status=MissionStatus.MISSION_STATUS_REWARDED,missionInfo=new(),rewardedCoinInfo=new(){gold=(uint)grant.Gold,cashPoint=(uint)grant.Cash},rewardExp=(uint)exp,rewardStamina=(uint)grant.Stamina,rewardTitleId=title,needRefresh=true};
            (p.Exp,p.Level,p.Stamina)=growth.Value;ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,r.rewardItemInfoList,r.rewardCardInfoList);
            if(title!=0)GrantTitle(p,title);
            p.Missions.DicePoints+=dice;
            (Daily(row)?p.Missions.DailyClaimed:p.Missions.Claimed)[claim.missionId]=Now;
            r.nowCoinInfo=Coins(p);r.exp=p.Exp;r.level=p.Level;r.stamina=p.Stamina;r.staminaStamp=p.StaminaStamp;r.staminaMax=catalog.StaminaMax(p.Level);
            return ok(r);
        },fail);
    }
    // Gameplay handlers emit these facts inside the same save batch as their mutations.
    private void RecordObjective(LocalPlayer player, C type, uint amount = 1)
    {
        foreach (var counters in new[] { player.Missions.Counters, player.Missions.DailyCounters })
            counters[type] = (uint)Math.Min(uint.MaxValue, (ulong)counters.GetValueOrDefault(type) + amount);
        AddActivityProgress(player, type.ToString(), amount);
        store.Save();
    }

    private void RecordResourceObjective(LocalPlayer player, uint stage, bool cleared, uint count = 1)
    {
        RecordObjective(player, cleared ? C.MCT_CLEAR_RESOURCE_DUNGEON : C.MCT_ENTER_RESOURCE_DUNGEON, count);
        if (BoardResources.Resources.Any(row => row.Id == stage && row.DungeonType == BscTable.RESOURCE_DUNGEON_TYPE.DUNGEON_SOKSUNG))
            RecordObjective(player, cleared ? C.MCT_CLEAR_SOKSUNG_DUNGEON : C.MCT_ENTER_SOKSUNG_DUNGEON, count);
    }

    private void RecordAdventureDeckObjective(LocalPlayer player, uint stageId, BscTable.GAME_DIFFICULTY difficulty, uint deckId)
    {
        if (difficulty is not (BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL or BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD) ||
            !catalog.StagesFor(difficulty).TryGetValue(stageId, out var stage)) return;
        var kind = difficulty == BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL ? C.MCT_ENTER_WITH_DECK_AT_NORMAL_STAGE : C.MCT_ENTER_WITH_DECK_AT_HARD_STAGE;
        var deck = DeckSlots(player, deckId).Where(seq => Owns(player, seq)).ToArray();
        uint level = (uint)stage.GetMobLevel(BscTable.GAME_MODE.MODE_NORMAL, difficulty);
        foreach (var row in Missions.Rows.Values.Where(x => x.CheckType == kind && x.Check1Id == stageId && MissionOpen(player, x)))
        {
            if (!Missions.Decks.TryGetValue(row.Check2Id, out var required)) continue;
            bool fits = required.AICardInfoList.All(ai => catalog.FindCard(ai.CardId) is {} target &&
                deck.Any(seq => catalog.FindCard(player.CardIds[(int)seq - 1]) is {} owned &&
                    owned.EvolveFirstAncestor == target.EvolveFirstAncestor && owned.Rank >= target.Rank && CardState(player, seq).Level >= level));
            if (fits && player.Missions.EnteredDeckMissions.Add((uint)row.MissionId)) store.Save();
        }
    }

    private void ObserveMissionSuccess(LocalPlayer player, object? request, object? response)
    {
        if (request is GsCastDiceBattleBoardRequest && response is GsCastDiceBattleBoardOk)
            RecordObjective(player, C.MCT_CAST_BATTLE_BOARD_DICE);
        if (response is GsCheckGuildAttendanceOk) RecordObjective(player, C.MCT_LOGIN_GUILD_ATTENDANCE);
        if (request is GsMovePawnBattleBoardRequest && response is GsMovePawnBattleBoardOk moved && moved.playInfo.status == BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_COMPLETE)
            RecordObjective(player, C.MCT_REACH_END_SPOT_AT_BATTLE_BOARD);
        if (request is GsGachaRequest draw && response is GsGachaOk)
        {
            var set = AvailableGachaSets.FirstOrDefault(x => x.GachaCategoryIndex == draw.gachaGroupIndex);
            if (set != null && draw.gachaGroupIndex <= 5)
                RecordObjective(player, C.MCT_GACHA, draw.useFreeCount ? 1u : (uint)Math.Max(1, set.GachaCount));
        }
    }
}
