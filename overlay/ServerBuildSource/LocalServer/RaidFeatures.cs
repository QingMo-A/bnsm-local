using System.Security.Cryptography;
using System.Text.Json;

internal sealed class LocalRaidRankCard
{
    public uint Id {get;set;}
    public uint Level {get;set;}
    public uint Enchant {get;set;}
    public uint Skin {get;set;}
    public PKSimpleCardInfo Pack()=>new(){cardId=Id,cardLevel=Level,cardEnchantGrade=Enchant,cardSkinId=Skin};
}
internal sealed class LocalRaidState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public Dictionary<int,uint> Used {get;set;}=[];
    public Dictionary<int,uint> Bought {get;set;}=[];
    public Dictionary<int,ulong> Latest {get;set;}=[];
    public Dictionary<int,uint> CoolResets {get;set;}=[];
    public Dictionary<uint,uint> BossHp {get;set;}=[];
    public Dictionary<uint,uint> BestDamage {get;set;}=[];
    public Dictionary<uint,List<LocalRaidRankCard>> BestDecks {get;set;}=[];
    public uint ListResets {get;set;}
    public int ActiveMode {get;set;}
    public uint ActiveBoss {get;set;}
    public ulong ActiveStamp {get;set;}
    public uint ActiveHp {get;set;}
    public uint ActiveLevel {get;set;}
    public uint CastleOwner {get;set;}
    public uint CastleMap {get;set;}
    public uint CastleSpot {get;set;}
    public ulong CastleGeneration {get;set;}
    public List<ulong> BattleCards {get;set;}=[];
    public List<ulong> BattleSideCards {get;set;}=[];
    public bool RosterSnapshot {get;set;}
    public PrivateRaidBattleType PrivateType {get;set;}
}

internal sealed partial class ProtocolRouter
{
    private RaidCatalog? raidCatalog;
    private RaidCatalog Raids=>raidCatalog??=new(RootDirectory);
    private void RefreshRaids(LocalPlayer p)
    {
        ulong day=TowerDay(Now);var r=p.Raids;
        // An unfinished encounter keeps its HP snapshot over the 04:00 boundary.
        if(r.Day==0||day>r.Day){r.Day=day;r.Used.Clear();r.Bought.Clear();r.Latest.Clear();r.CoolResets.Clear();r.BossHp.Clear();r.ListResets=0;store.Save();}
    }
    private uint RaidFree(LocalPlayer p,int mode)
    {
        var v=catalog.VipFor(p.VipExp);return (uint)Math.Max(0,mode switch{1=>v.RaidBossEnterCount,2=>v.WorldBossEnterCount,3=>v.ComboBossEnterCount,4=>v.BigBossEnterCount,_=>0});
    }
    private uint RaidLeft(LocalPlayer p,int mode)=>(uint)Math.Max(0,(long)RaidFree(p,mode)+p.Raids.Bought.GetValueOrDefault(mode)-p.Raids.Used.GetValueOrDefault(mode));
    private uint RaidWorldId(LocalPlayer p,bool combo)
    {
        var ids=Raids.Schedules.Where(x=>x.ComboMode==combo).Select(x=>x.BossId).Distinct().Order().ToArray();
        return (uint)ids[(int)(p.Raids.Day%(ulong)ids.Length)];
    }
    private IEnumerable<uint> PrivateRaidIds(LocalPlayer p)=>catalog.HardStageRows.Where(x=>x.Id<=p.ClearHardStage&&x.GenRaidMobId>0).Select(x=>(uint)x.GenRaidMobId).Distinct();
    private BscTable.CONTENT_OPEN_TYPE RaidGate(int mode)=>mode switch{1=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_DUNGEON_EVIL,2=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_WORLDBOSS,3=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_WORLDBOSS_COMBO,_=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_BIG_BOSS};
    private uint RaidCooldown(int mode)=>(uint)Math.Max(0,catalog.Numbers.GetValueOrDefault(mode==4?BscTable.NUMBER_TYPE.BIG_BOSS_BATTLE_COOLTIME_SEC:mode==3?BscTable.NUMBER_TYPE.WORLD_BOSS_COMBO_BATLE_COOLTIME_SEC:BscTable.NUMBER_TYPE.WORLD_BOSS_BATLE_COOLTIME_SEC));
    private GsGetWorldBossInfoOk WorldRaidInfo(LocalPlayer p)=>new(){worldBossId=RaidWorldId(p,false),comboBossId=RaidWorldId(p,true),worldBossBattleCount=RaidLeft(p,2),buyWorldBossBattleCount=p.Raids.Bought.GetValueOrDefault(2),latestWorldBossBattleTime=(long)p.Raids.Latest.GetValueOrDefault(2),worldBossCoolTimeResetCount=p.Raids.CoolResets.GetValueOrDefault(2),comboBossBattleCount=RaidLeft(p,3),buyComboBossBattleCount=p.Raids.Bought.GetValueOrDefault(3),latestComboBossBattleTime=(long)p.Raids.Latest.GetValueOrDefault(3),comboBossCoolTimeResetCount=p.Raids.CoolResets.GetValueOrDefault(3)};
    private PKCheckGameEntryData RaidEntry(LocalPlayer p)
    {
        var ids=PrivateRaidIds(p).ToList();
        var r=new PKCheckGameEntryData{hasNewEventRewardFlag=ActivityHasRewards(p),trainingBadgeFlag=p.TrainingSlots.Values.Any(x=>x.CardSequence!=0&&(x.Fast||x.EndTime<=Now)),skillPointRechargeCount=(uint)p.SkillPointChargeCount,challengeUsedEnterCount=p.ChallengeTower.UsedEntries,attendanceInfo=AttendanceInfo(p),privateRaidBattleCount=RaidLeft(p,1),todayRaidBossBattleBuyCount=p.Raids.Bought.GetValueOrDefault(1),worldBossExists=1,worldBossBattleCount=RaidLeft(p,2)};
        r.RaidBossID.AddRange(ids);r.remainRaidBossCount=(uint)ids.Count;r.raidBossStatus=ids.Count>0?(PrivateRaidBossStatus)1:(PrivateRaidBossStatus)0;
        return r;
    }
    private IEnumerable<BscTable.CardData> BigRaidCards(LocalPlayer p)=>Raids.Big.Where(x=>x.BossGrade>0&&(int)x.BossGrade<=(p.Level>=catalog.Numbers[BscTable.NUMBER_TYPE.BIG_BOSS_RECALL_LEVEL_HERO]?3:p.Level>=catalog.Numbers[BscTable.NUMBER_TYPE.BIG_BOSS_RECALL_LEVEL_HARD]?2:1));
    private GsGetBigBossListOk BigRaidList(LocalPlayer p)
    {
        var result=new GsGetBigBossListOk();foreach(var card in BigRaidCards(p)){
            uint hp=Raids.Hp(card,(uint)card.Level);result.bigBossIDList.Add((uint)card.Id);result.bigBossHPList.Add(p.Raids.BossHp.GetValueOrDefault((uint)card.Id,hp));result.bigBossFullHPList.Add(hp);result.recallFlag.Add(true);
        }return result;
    }
    private Reply FeatureReceipt(LocalPlayer p,Dictionary<string,LocalMutationReceipt> receipts,string name,SessionKeyInfo session,byte[] payload,Func<Reply> execute,Func<string,ServerErrorCode,Reply> fail,bool mutation=true)
    {
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}"))),fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(mutation&&receipts.TryGetValue(key,out var old)) {
            if(old.Fingerprint!=fingerprint)return fail("request sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
            if(name is "GS_WORLD_WAR_READY_REQUEST" or "GS_WORLD_WAR_START_REQUEST"){
                if(old.WorldBattleStamp==0||p.Competitive.ActiveMode!=44||p.Competitive.Stamp!=old.WorldBattleStamp||name=="GS_WORLD_WAR_START_REQUEST"&&(!p.Competitive.Started||p.ActiveBattleStamp!=old.WorldBattleStamp))
                    return fail("world-war cached preparation or start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            }
            if(name=="GS_WORLD_WAR_END_CONFIRM_REQUEST"&&(old.WorldBattleStamp==0||p.Competitive.Stamp!=0&&p.Competitive.Stamp!=old.WorldBattleStamp||p.ActiveBattleStamp!=0&&p.ActiveBattleStamp!=old.WorldBattleStamp))
                return fail("old world-war completion cannot replace the current battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            var message=string.IsNullOrEmpty(old.TypeName)?null:WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(old.TypeName)??typeof(ProtocolRouter).Assembly.GetType(old.TypeName,true),Convert.FromBase64String(old.Payload));
            if(message is LocalChainBattleStartOk chainStart&&(p.Competitive.ActiveMode!=16||p.Competitive.Stamp!=chainStart.stamp||p.ActiveBattleStamp!=chainStart.stamp))return fail("武神之路开战回执已过期",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(name.Contains("CHAIN_BATTLE",StringComparison.Ordinal)&&message!=null)
            {
                var recordField=message.GetType().GetField("chainBattleRecord");
                if(recordField?.GetValue(message) is PKChainBattleRecord record&&
                    (!WireCodec.Serialize(record).SequenceEqual(WireCodec.Serialize(ChainRecord(p)))||p.Competitive.ActiveMode==16))
                    return fail("武神之路状态已变化，请刷新后重试",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            }
            if(message is GsWanderBattleStartOk prior&&!RaidEndMatches(p,5,prior.wanderBattleRecord.spotId,prior.startTimeStamp))return fail("wander start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_ID);
            if(message is GsBigBossBattleStartOk bossPrior&&!RaidEndMatches(p,4,bossPrior.bossID,bossPrior.timeStamp))return fail("boss start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsStartFriendRaidBattleOk friendPrior&&(p.Raids.ActiveMode is not (6 or 7)||p.Raids.ActiveStamp!=friendPrior.timeStamp||p.ActiveBattleStamp!=friendPrior.timeStamp))return fail("castle monster start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsEndFriendRaidBattleOk&&p.ActiveBattleStamp!=0&&WireCodec.Deserialize(typeof(GsEndFriendRaidBattleRequest),payload) is GsEndFriendRaidBattleRequest oldEnd&&p.ActiveBattleStamp!=oldEnd.timeStamp)return fail("old castle monster completion cannot replace the current battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsFriendRaidBossSummonOk summonPrior){var raid=CastleSpot(p,summonPrior.mapId,summonPrior.spotId)?.Raid;if(raid==null||raid.State!=FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE||raid.SummonDay!=TowerDay(Now)||raid.Summons!=summonPrior.todaySummonCount||raid.History.Count>0)return fail("castle monster summon receipt expired; refresh the map",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);}
            if(message is GsRaidBattleStartOk raidPrior&&!RaidEndMatches(p,1,raidPrior.raidBossID,raidPrior.timeStamp))return fail("raid start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsStartWorldBossBattleOk worldPrior&&(p.Raids.ActiveStamp!=(ulong)worldPrior.timeStamp||p.ActiveBattleStamp!=(ulong)worldPrior.timeStamp))return fail("world boss start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsEndWorldBossBattleOk&&p.ActiveBattleStamp!=0&&WireCodec.Deserialize(typeof(GsEndWorldBossBattleRequest),payload) is GsEndWorldBossBattleRequest oldWorldEnd&&p.ActiveBattleStamp!=(ulong)oldWorldEnd.timeStamp)return fail("old world boss completion cannot replace the current battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsResourceStageBattleStartOk resourcePrior&&(p.Resources.ActiveStamp!=resourcePrior.timeStamp||p.ActiveBattleStamp!=resourcePrior.timeStamp))return fail("resource start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_ID);
            if(message is GsGetFactionArenaBattleEnemyInfoOk factionPrior&&(p.Faction.Opponent==0||p.Faction.Opponent!=factionPrior.enemyInfo.rankInfo.userInfo.uid))return fail("faction preview belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_ID);
            if(name=="GS_BATTLE_BOARD_START_REQUEST"&&(p.Board.ActiveStamp==0||p.Board.ActiveStamp!=p.ActiveBattleStamp))return fail("board start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_ID);
            if(message is LocalCastleEncounterReply castlePrior&&(p.Competitive.ActiveMode!=18||p.Competitive.Stamp!=castlePrior.stamp||p.Competitive.Started))return fail("castle preview belongs to an ended or replaced encounter",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is LocalRankCombatReply {operation:4} castleStart&&(p.Competitive.ActiveMode!=18||p.Competitive.Stamp!=castleStart.stamp||p.ActiveBattleStamp!=castleStart.stamp))return fail("castle start belongs to an ended or replaced encounter",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is LocalRankCombatReply {operation:1} pvpStart&&(p.Competitive.Stamp!=pvpStart.stamp||p.ActiveBattleStamp!=pvpStart.stamp))return fail("competitive start belongs to an ended or replaced battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is LocalRankCombatReply {operation:2 or 3} pvpDone&&((p.Competitive.Stamp!=0&&p.Competitive.Stamp!=pvpDone.stamp)||(p.ActiveBattleStamp!=0&&p.ActiveBattleStamp!=pvpDone.stamp)))return fail("old competitive completion cannot replace the current battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is LocalRankCombatReply {operation:11 or 12 or 14} castleDone&&((p.Competitive.Stamp!=0&&p.Competitive.Stamp!=castleDone.stamp)||(p.ActiveBattleStamp!=0&&p.ActiveBattleStamp!=castleDone.stamp)))return fail("old castle completion cannot replace the current battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(message is GsGetCastleBattleAttackMatchOk searchPrior&&(p.ActiveStage!=null||p.Competitive.CastleTarget!=searchPrior.targetUserInfo.uid||p.Competitive.CastleSearches!=searchPrior.attackMatchTryCount))return fail("castle search result expired; search again",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            return new(old.Command,message,old.Name);
        }
        ulong worldStamp=p.Competitive.ActiveMode==44?p.Competitive.Stamp:0;
        var reply=execute();if(mutation&&reply.Name.EndsWith("_OK",StringComparison.Ordinal)){
            if(receipts.Count>=256)receipts.Remove(receipts.Keys.First());
            receipts[key]=new(){WorldBattleStamp=name=="GS_WORLD_WAR_END_CONFIRM_REQUEST"?worldStamp:p.Competitive.ActiveMode==44?p.Competitive.Stamp:0,Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message?.GetType().FullName??"",Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};store.Save();
        }return reply;
    }
    private bool RaidRoster(LocalPlayer p,int mode)
    {
        var cards=DeckSlots(p,(uint)(mode switch{1=>13,2=>23,3=>24,4=>21,_=>22})).Where(x=>x!=0).ToList();
        if(cards.Count==0||cards.Count>5||cards.Distinct().Count()!=cards.Count||cards.Any(x=>!Owns(p,x)||InTraining(p,x)))return false;
        // BscDeckDialogBigBossRaid offers every faction and accepts a nonempty
        // roster. Do not turn BigBossStageLink.EnterType into an additional
        // server-only faction gate (it rejected Banji and Jinseoyeon decks).
        p.Raids.BattleCards=cards;return true;
    }
    private bool RaidEndMatches(LocalPlayer p,int mode,uint id,ulong stamp)=>p.Raids.ActiveMode==mode&&p.Raids.ActiveBoss==id&&p.Raids.ActiveStamp==stamp&&LocalBattleAttempts.Matches(p,id,stamp);
    private void ClearRaidBattle(LocalPlayer p)
    {
        var raid = p.Raids;
        LocalBattleAttempts.ClearMatching(p, raid.ActiveBoss, raid.ActiveStamp);
        raid.ActiveMode = 0;
        raid.ActiveStamp = 0;
        raid.ActiveBoss = 0;
        raid.BattleCards.Clear();
        raid.BattleSideCards.Clear();
        raid.RosterSnapshot = false;
    }
    private bool RaidRewards(LocalPlayer p,ShopGrant grant,List<BscTable.NPC_REWARD_TYPE> types,List<int> ids,List<int> counts,List<double>? probabilities=null)
    {
        if(types.Count!=ids.Count||ids.Count!=counts.Count)return false;
        for(int i=0;i<ids.Count;i++){
            if(probabilities?.Count>0&&(probabilities.Count!=ids.Count||Random.Shared.NextDouble()*100>=probabilities[i]))continue;
            if(!AddStageRow(p,grant,types[i],ids[i],counts[i]))return false;
        }return true;
    }
    private Reply? HandleRaids(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_GET_FRIEND_RAID_PLAY_COUNT_REQUEST"){RefreshRaids(p);return ok(new GsGetFriendRaidCountOk{todayFriendRaidCount=FriendRaidLeft(p),buyFriendRaidCount=p.Raids.Bought.GetValueOrDefault(6)});}
        if(name is not ("GS_GET_WORLD_BOSS_INFO_REQUEST" or "GS_GET_BIG_BOSS_PLAY_COUNT_REQUEST" or "GS_GET_BIG_BOSS_LIST_REQUEST" or "GS_RESET_BIG_BOSS_LIST_REQUEST" or "GS_RECALL_RAID_BOSS_REQUEST" or "GS_RAID_BATTLE_START_REQUEST" or "GS_RAID_BATTLE_END_REQUEST" or "GS_START_WORLD_BOSS_BATTLE_REQUEST" or "GS_END_WORLD_BOSS_BATTLE_REQUEST" or "GS_BIG_BOSS_BATTLE_START_REQUEST" or "GS_BIG_BOSS_BATTLE_END_REQUEST" or "GS_BUY_BONUS_COUNT_REQUEST" or "GS_BUY_PRIVATE_RAID_PLAY_COUNT_REQUEST" or "GS_BUY_WORLD_BOSS_PLAY_COUNT_REQUEST" or "GS_BUY_BIG_BOSS_PLAY_COUNT_REQUEST" or "GS_RESET_WORLD_BOSS_COOLTIME_REQUEST" or "GS_RESET_BIG_BOSS_COOLTIME_REQUEST" or "GS_GET_WORLD_BOSS_TOP_RANKER_REQUEST"))return null;
        return FeatureReceipt(p,p.RaidReceipts,name,session,payload,()=>ExecuteRaids(name,request,p,ok,fail),fail,!name.StartsWith("GS_GET_"));
    }
    private Reply ExecuteRaids(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var r=p.Raids;Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);
        // The native lobby refreshes when the deck hides after start admission.
        // A list/count read is not a return-to-lobby or retreat acknowledgement.
        if(name=="GS_GET_WORLD_BOSS_INFO_REQUEST")return ok(WorldRaidInfo(p));
        if(name=="GS_GET_BIG_BOSS_PLAY_COUNT_REQUEST")return ok(new GsGetBigBossCountOk{bigBossBattleCount=RaidLeft(p,4),buyBigBossBattleCount=r.Bought.GetValueOrDefault(4),bigBossResetCount=r.ListResets,latestBattleTime=r.Latest.GetValueOrDefault(4),coolTimeResetCount=r.CoolResets.GetValueOrDefault(4),userRankData=new()});
        if(name=="GS_GET_BIG_BOSS_LIST_REQUEST"){if(r.ActiveMode==4)AbandonRaidBattle(p);return ok(BigRaidList(p));}
        if(name=="GS_RECALL_RAID_BOSS_REQUEST") {if(r.ActiveMode==1)AbandonRaidBattle(p);var recall=new GsRecallRaidBossOk{recallTime=Now,privateRaidBattleCount=RaidLeft(p,1)};recall.bossID.AddRange(PrivateRaidIds(p));return ok(recall);}
        if(p.ActiveStage!=null&&!name.Contains("BATTLE_END")&&!name.Contains("END_WORLD_BOSS"))return fail("finish the active battle first",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        if(name is "GS_BUY_BONUS_COUNT_REQUEST" or "GS_BUY_PRIVATE_RAID_PLAY_COUNT_REQUEST" or "GS_BUY_WORLD_BOSS_PLAY_COUNT_REQUEST" or "GS_BUY_BIG_BOSS_PLAY_COUNT_REQUEST")return BuyRaidEntries(name,request,p,ok,fail);
        if(name=="GS_RESET_BIG_BOSS_LIST_REQUEST"){
            if(!FeatureOpen(p,RaidGate(4))||RaidLeft(p,4)==0||r.ListResets>=100)return Invalid("boss list refresh is unavailable");
            var fee=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_BIGBOSS_LIST,(int)r.ListResets+1,true);
            if(fee==null||fee.Value<0||fee.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_CASH||p.CashPoint<fee.Value)return fail("not enough diamonds to refresh bosses",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)fee.Value;r.BossHp.Clear();r.ListResets++;var list=BigRaidList(p);var reset=new GsResetBigBossListOk{bigBossResetCount=r.ListResets,nowCoinInfo=Coins(p)};reset.bigBossIDList.AddRange(list.bigBossIDList);reset.bigBossHPList.AddRange(list.bigBossHPList);reset.bigBossFullHPList.AddRange(list.bigBossFullHPList);reset.recallFlag.AddRange(list.recallFlag);return ok(reset);
        }
        if(name is "GS_RESET_WORLD_BOSS_COOLTIME_REQUEST" or "GS_RESET_BIG_BOSS_COOLTIME_REQUEST"){
            int mode=request is GsResetWorldBossCoolTimeRequest cool?(cool.isCombo?3:2):4;
            if(!FeatureOpen(p,RaidGate(mode))||RaidCooldown(mode)==0||r.Latest.GetValueOrDefault(mode)+RaidCooldown(mode)<=Now)return Invalid("boss is not cooling down");
            var type=mode==4?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_BIGBOSS_COOLTIME:mode==3?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_COMBOBOSS_COOLTIME:BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_WORLDBOSS_COOLTIME;
            var fee=catalog.Expenses.FindItem(type,(int)r.CoolResets.GetValueOrDefault(mode)+1,true);if(fee==null||fee.Value<=0||p.CashPoint<fee.Value)return fail("not enough cooldown-reset diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)fee.Value;r.Latest[mode]=0;r.CoolResets[mode]=r.CoolResets.GetValueOrDefault(mode)+1;
            return mode==4?ok(new GsResetBigBossCoolTimeOk{nowCoinInfo=Coins(p),coolTimeResetCount=r.CoolResets[mode]}):ok(new GsResetWorldBossCoolTimeOk{nowCoinInfo=Coins(p),coolTimeResetCount=r.CoolResets[mode]});
        }
        if(name is "GS_RAID_BATTLE_START_REQUEST" or "GS_START_WORLD_BOSS_BATTLE_REQUEST" or "GS_BIG_BOSS_BATTLE_START_REQUEST"){
            int mode=request is GsRaidBattleStartRequest?1:request is GsStartWorldBossBattleRequest w?(w.isCombo?3:2):4;
            uint id=request is GsRaidBattleStartRequest pr?pr.raidBossID:request is GsStartWorldBossBattleRequest wr?wr.bossId:((GsBigBossBattleStartRequest)request!).bossID;
            if(!FeatureOpen(p,RaidGate(mode)))return fail("original boss unlock level not reached",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            var card=(mode==1?Raids.Private:mode==4?BigRaidCards(p):Raids.World).FirstOrDefault(x=>x.Id==id);
            if(card==null||mode==1&&!PrivateRaidIds(p).Contains(id)||mode is 2 or 3&&id!=RaidWorldId(p,mode==3))return Invalid("boss is unavailable or not encountered in hard adventure");
            if(request is GsRaidBattleStartRequest ps&&!Enum.IsDefined(ps.battleType))return Invalid("invalid private raid entry type");
            if(RaidLeft(p,mode)==0)return fail("no daily boss entries remaining",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(r.Latest.GetValueOrDefault(mode)+RaidCooldown(mode)>Now)return fail("boss entry is cooling down; remainingSeconds="+(r.Latest.GetValueOrDefault(mode)+RaidCooldown(mode)-Now),mode==4?ServerErrorCode.ERROR_BIG_BOSS_BATTLE_COOLTIME:ServerErrorCode.ERROR_WORLD_BOSS_BATTLE_COOLTIME);
            uint full=Raids.Hp(card,mode is 2 or 3?p.Level:(uint)card.Level),hp=mode==4?r.BossHp.GetValueOrDefault(id,full):full;
            if(hp==0)return fail("this boss is already defeated",ServerErrorCode.ERROR_BOSS_IS_ALREADY_CLEARED);
            r.ActiveBoss=id;if(!RaidRoster(p,mode)){r.ActiveBoss=0;return fail("invalid, missing or training cards in the boss deck",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);}
            r.ActiveMode=mode;r.ActiveHp=hp;r.ActiveLevel=p.Level;r.ActiveStamp=NextLocalBattleStamp(p);LocalBattleAttempts.Bind(p,id,r.ActiveStamp);
            if(request is GsRaidBattleStartRequest privateStart)r.PrivateType=privateStart.battleType;
            r.Used[mode]=r.Used.GetValueOrDefault(mode)+1;r.Latest[mode]=Now;
            if (mode == 4) RecordObjective(p, BscTable.CHECK_TYPE.MCT_ENTER_BIG_BOSS);
            return mode==1?ok(new GsRaidBattleStartOk{raidBossID=id,timeStamp=r.ActiveStamp}):mode==4?ok(new GsBigBossBattleStartOk{bossID=id,timeStamp=r.ActiveStamp}):ok(new GsStartWorldBossBattleOk{timeStamp=(long)r.ActiveStamp});
        }
        if(request is GsRaidBattleEndRequest end){
            if(!RaidEndMatches(p,1,end.raidBossID,end.timeStamp)||end.battleType!=r.PrivateType||end.endType>1)return fail("no matching private raid battle",ServerErrorCode.ERROR_INVALID_BIG_BOSS_BATTLE_STATUS);
            bool won=end.endType==0;if(won&&end.damageAmount<r.ActiveHp)return Invalid("victory damage is below boss HP");
            var grant=new ShopGrant();uint exp=0;
            if(won){var reward=Raids.PrivateRewards.SingleOrDefault(x=>x.RaidMonId==end.raidBossID);if(reward==null)return Invalid("original private raid reward missing");exp=(uint)reward.RewardExp;grant.Gold=(uint)reward.Gold;grant.Cash=(uint)reward.Gem;grant.World=(uint)reward.WorldBossPoint;if(!RaidRewards(p,grant,reward.NpcRewardIdType,reward.NpcRewardId,reward.RewardQuantity,reward.DropProb)||reward.CardRewardExp>0&&!AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,210002001,reward.CardRewardExp))return Invalid("private raid reward unavailable");}
            var growth=StageGrowth(p,exp,0);if(growth==null||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("raid reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsRaidBattleEndOk{earnedCoinInfo=GrantCoins(grant),rewardExp=exp,privateRaidBattleCount=RaidLeft(p,1),battleEndSyncData=new()};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.deltaItem,result.rewardCard);p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;result.level=p.Level;result.nowExp=p.Exp;result.nowCoinInfo=Coins(p);ClearRaidBattle(p);return ok(result);
        }
        if(request is GsEndWorldBossBattleRequest worldEnd){
            int mode=worldEnd.isCombo?3:2;if(!RaidEndMatches(p,mode,worldEnd.bossId,(ulong)worldEnd.timeStamp))return fail($"world boss settlement mismatch: requestedMode={mode} boss={worldEnd.bossId} stamp={worldEnd.timeStamp}; activeMode={r.ActiveMode} boss={r.ActiveBoss} stamp={r.ActiveStamp} stage={p.ActiveStage} globalStamp={p.ActiveBattleStamp}",ServerErrorCode.ERROR_INVALID_BIG_BOSS_BATTLE_STATUS);
            var row=Raids.WorldRewards.Single(x=>x.PCLevel==r.ActiveLevel);
            // BattleCard.ApplyResult intentionally leaves WORLD_BOSS.CurrentHp
            // unchanged. Total damage can exceed its base HP during the timer.
            uint damage=Math.Min(worldEnd.damageAmount,(uint)catalog.Numbers[BscTable.NUMBER_TYPE.WORLD_BOSS_BATTLE_INTEGRITY_VALUE]);
            uint points=(uint)Math.Min((ulong)row.BossPointLimit,(ulong)row.BossPointDiff+(ulong)(damage/(uint)Math.Max(1,row.RequireDamage))*(uint)row.BossPointRatio);
            if(damage==0)points=0;if((ulong)p.WorldBossPoint+points>uint.MaxValue)return fail("essence storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            p.WorldBossPoint+=points;
            if(r.ActiveMode==3)p.Ranking.ComboBest=Math.Max(p.Ranking.ComboBest,damage);else p.Ranking.WorldBest=Math.Max(p.Ranking.WorldBest,damage);
            if(damage>r.BestDamage.GetValueOrDefault(worldEnd.bossId)){
                r.BestDamage[worldEnd.bossId]=damage;r.BestDecks[worldEnd.bossId]=r.BattleCards.Select(seq=>{var state=CardState(p,seq);return new LocalRaidRankCard{Id=p.CardIds[(int)seq-1],Level=state.Level,Enchant=state.Enchant,Skin=state.Skin};}).ToList();
            }
            ClearRaidBattle(p);return ok(new GsEndWorldBossBattleOk{earnedCoinInfo=new(){worldBossPoint=points},nowCoinInfo=Coins(p),battleEndSyncData=new()});
        }
        if(request is GsBigBossBattleEndRequest bigEnd){
            if(!RaidEndMatches(p,4,bigEnd.bossID,bigEnd.timeStamp))return fail("no matching big boss battle",ServerErrorCode.ERROR_INVALID_BIG_BOSS_BATTLE_STATUS);
            var card=Raids.Big.Single(x=>x.Id==bigEnd.bossID);uint full=Raids.Hp(card,(uint)card.Level),hp=r.ActiveHp-Math.Min(r.ActiveHp,bigEnd.damageAmount),before=full-r.ActiveHp,after=full-hp;
            var row=Raids.BigRewards.Single(x=>x.Id==card.BigBossRewardId);var grants=new List<(uint Index,ShopGrant Grant)>();uint exp=0;var total=new ShopGrant();
            for(int i=0;i<row.RewardInfoList.Count;i++){
                var reward=row.RewardInfoList[i];if(reward.CumulativeDamage<=before||reward.CumulativeDamage>after)continue;
                var grant=new ShopGrant{Gold=(uint)reward.Gold,Cash=(uint)reward.Gem,World=(uint)reward.WorldBossPoint};
                if(!RaidRewards(p,grant,reward.NpcRewardIdType,reward.NpcRewardId,reward.RewardQuantity)||reward.CardRewardExp>0&&!AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,210002001,reward.CardRewardExp))return Invalid("original big boss reward unavailable");
                exp+=(uint)reward.RewardExp;grants.Add(((uint)i+1,grant));total.Gold+=grant.Gold;total.Cash+=grant.Cash;total.World+=grant.World;foreach(var pair in grant.Items)total.Items[pair.Key]=total.Items.GetValueOrDefault(pair.Key)+pair.Value;total.Cards.AddRange(grant.Cards);
            }
            var growth=StageGrowth(p,exp,0);if(growth==null||!CanPayAndGrant(p,total,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("big boss reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsBigBossBattleEndOk{earnedCoinInfo=GrantCoins(total),rewardExp=exp,bigBossBattleCount=RaidLeft(p,4),buyBigBossBattleCount=r.Bought.GetValueOrDefault(4),bossHp=hp,bossFullHp=full,battleEndSyncData=new()};
            foreach(var (index,grant) in grants){var reward=new PKBigBossBattleReward{sequence=index,earnedCoinInfo=GrantCoins(grant)};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,reward.earnedItemList,reward.earnedCardList);result.battleReward.Add(reward);}
            p.Ranking.BigBossDamage+=Math.Min(r.ActiveHp,bigEnd.damageAmount);p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;r.BossHp[bigEnd.bossID]=hp;result.level=p.Level;result.nowExp=p.Exp;result.nowCoinInfo=Coins(p);ClearRaidBattle(p);return ok(result);
        }
        return Invalid("unrecognized boss operation");
    }
    private static PKCoinInfo GrantCoins(ShopGrant g)=>new(){gold=(uint)g.Gold,cashPoint=(uint)g.Cash,worldBossPoint=(uint)g.World,guildToken=(uint)g.Tokens};
    private Reply BuyRaidEntries(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        int type=request is GsBuyBonusCountRequest generic?(int)generic.priceType:request is GsBuyPrivateRaidCountRequest?9:request is GsBuyWorldBossCountRequest world?(world.isCombo?11:10):8;
        uint count=request is GsBuyBonusCountRequest g?g.buyCount:request is GsBuyWorldBossCountRequest w?w.buyCount:request is GsBuyBigBossCountRequest b?b.buyCount:1;
        if(type==12)return BuyWanderEntries(p,count,ok,fail,name);
        if(type==13)return BuyFriendRaidEntries(p,count,ok,fail);
        int mode=type switch{8=>4,9=>1,10=>2,11=>3,_=>0};if(mode==0||!FeatureOpen(p,RaidGate(mode))||count==0)return fail("invalid boss ticket purchase",ServerErrorCode.ERROR_INVALID_ID);
        var vip=catalog.VipFor(p.VipExp);uint max=(uint)Math.Max(0,mode switch{1=>vip.RaidBossEnterBonusCount,2=>vip.WorldBossEnterBonusCount,3=>vip.ComboBossEnterBonusCount,_=>vip.BigBossEnterBonusCount});
        if((ulong)p.Raids.Bought.GetValueOrDefault(mode)+count>max)return fail("daily VIP boss ticket limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var prices=Raids.Prices.Single(x=>(int)x.VipRiceDiscountType==type).PriceDiscountBuyCountInfoList;var price=prices.SingleOrDefault(x=>x.Count==(int)count)??prices.SingleOrDefault(x=>x.Count==0);
        ulong cost=price==null?ulong.MaxValue:price.Count==0?(ulong)price.Price*count:(uint)price.Price;
        if(cost>p.CashPoint)return fail("not enough diamonds for boss tickets",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        p.CashPoint-=(uint)cost;p.Raids.Bought[mode]=p.Raids.Bought.GetValueOrDefault(mode)+count;uint bought=p.Raids.Bought[mode],left=RaidLeft(p,mode);
        return name=="GS_BUY_BONUS_COUNT_REQUEST"?ok(new GsBuyBonusCountOk{coinInfo=Coins(p),todayBuyCount=bought,changedValue=left,priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)type}):mode==1?ok(new GsBuyPrivateRaidCountOk{coinInfo=Coins(p),PrivateRaidRemainingCount=left,todayBuyCount=bought}):mode==4?ok(new GsBuyBigBossCountOk{coinInfo=Coins(p),bigBossBattleCount=left,buyBigBossBattleCount=bought}):ok(new GsBuyWorldBossCountOk{coinInfo=Coins(p),worldBossBattleCount=left,buyWorldBossBattleCount=bought});
    }
}
