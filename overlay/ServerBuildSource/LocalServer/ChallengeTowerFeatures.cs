using System.Text.Json;
using System.Security.Cryptography;

internal sealed class LocalChallengeTower
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public ulong Week {get;set;}
    public uint Points {get;set;}
    public uint UsedEntries {get;set;}
    public uint DailyFloor {get;set;}
    public uint BestFloor {get;set;}
    public uint BestArea {get;set;}
    public Dictionary<uint,byte> Areas {get;set;}=[];
    public HashSet<ulong> DeadCards {get;set;}=[];
    public ulong ActiveStamp {get;set;}
    public uint ActiveId {get;set;}
    public List<ulong> BattleCards {get;set;}=[];
    public List<uint> PendingWeeklyRewards {get;set;}=[];
}

internal sealed partial class ProtocolRouter
{
    internal static ulong TowerDay(ulong now)=>(now+4*3600)/86400; // UTC+8, 04:00.
    private void RefreshTower(LocalPlayer p)
    {
        var t=p.ChallengeTower;ulong day=TowerDay(Now),week=(day+3)/7;bool changed=false;
        if(t.Week!=0&&week>t.Week) {
            var reward=catalog.TowerPointRewards.Where(x=>t.Points>=x.Point_Min).OrderByDescending(x=>x.Point_Min).FirstOrDefault();
            if(reward!=null)t.PendingWeeklyRewards.Add((uint)reward.Id);
            t.Points=0;t.BestFloor=0;t.BestArea=0;t.Week=week;t.Day=0;changed=true;
        }
        if(t.Day==0||day>t.Day) {
            ClearTowerAttempt(p);
            p.TowerReceipts.Clear();
            t.Areas.Clear();t.DeadCards.Clear();t.UsedEntries=0;t.DailyFloor=0;t.Day=day;changed=true;
        }
        if(t.Week==0){t.Week=week;changed=true;}
        while(t.PendingWeeklyRewards.Count>0) {
            var reward=catalog.TowerPointRewards.Single(x=>x.Id==t.PendingWeeklyRewards[0]);var grant=new ShopGrant();bool valid=true;
            for(int i=0;i<reward.RewardId.Count;i++)valid&=AddShopReward(p,grant,reward.RewardIdType[i],reward.RewardId[i],reward.RewardQuantity[i]);
            if(!valid||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))break;
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,[],[]);t.PendingWeeklyRewards.RemoveAt(0);changed=true;
        }
        if(changed)store.Save();
    }
    private byte[] TowerAchievements(LocalPlayer p)
    {
        var data=new byte[(catalog.Towers.Max(x=>x.Id)+1)*6];
        foreach(var pair in p.ChallengeTower.Areas){int index=(int)(pair.Key/10)*6+(int)(pair.Key%10);if(index<data.Length)data[index]=pair.Value;}
        return data;
    }
    private HashSet<int> TowerBan(uint floor)
    {
        var row=catalog.Towers.Single(x=>x.Id==floor);var date=DateTimeOffset.FromUnixTimeSeconds((long)Now).ToOffset(TimeSpan.FromHours(8)).AddHours(-4);
        int id=date.DayOfWeek switch {DayOfWeek.Monday=>row.BanGroupId_Monday,DayOfWeek.Tuesday=>row.BanGroupId_Tuesday,DayOfWeek.Wednesday=>row.BanGroupId_Wednesday,DayOfWeek.Thursday=>row.BanGroupId_Thursday,DayOfWeek.Friday=>row.BanGroupId_Friday,DayOfWeek.Saturday=>row.BanGroupId_Saturday,_=>row.BanGroupId_Sunday};
        return catalog.TowerBans.Where(x=>x.Id==id||x.Id==row.BanGroupId_Always).SelectMany(x=>x.CTBanCardIdInfoList).Select(x=>x.BanCardId).ToHashSet();
    }
    private Reply? HandleChallengeTower(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_CHALLENGE_STAGE_LOBBY_REQUEST" or "GS_CHALLENGE_STAGE_BATTLE_START_REQUEST" or "GS_CHALLENGE_STAGE_BATTLE_END_REQUEST"))return null;
        var t=p.ChallengeTower;
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CHALLENGE_TOWER))return fail("challenge tower opens at original level 90",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(name=="GS_CHALLENGE_STAGE_LOBBY_REQUEST") {
            AbandonTowerBattle(p);
            var lobby=new GsChallengeStageLobbyOk{challengePoint=t.Points,challengeAchieveInfo=TowerAchievements(p),clearChallengeDifficulty=t.BestFloor,clearChallengeStage=t.BestArea};lobby.dieCardSeq.AddRange(t.DeadCards);return ok(lobby);
        }
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}"))),fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.TowerReceipts.TryGetValue(key,out var saved)) {
            if(saved.Fingerprint!=fingerprint)return fail("tower sequence reused with changed parameters",ServerErrorCode.ERROR_INVALID_ID);
            var message=WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(saved.TypeName,true),Convert.FromBase64String(saved.Payload));
            if(message is GsChallengeStageBattleStartOk prior&&(t.ActiveStamp!=prior.timeStamp||p.ActiveBattleStamp!=prior.timeStamp||t.ActiveId!=p.ActiveStage))return fail("tower start belongs to an ended or abandoned attempt",ServerErrorCode.ERROR_INVALID_ID);
            return new(saved.Command,message,saved.Name);
        }
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);
        Reply result;
        if(request is GsChallengeStageBattleStartRequest start) {
            var row=catalog.TowerStages.FirstOrDefault(x=>x.TowerId==start.difficulty&&x.AreaId==start.stageid);
            uint unlocked=Math.Min((uint)catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CHALLENGE_TOWER_FLOOR),Math.Max(1,t.BestFloor+(t.BestArea==5?1u:0u)));
            if(row==null)return fail("unknown tower stage",ServerErrorCode.ERROR_INVALID_STAGE_ID);
            if(start.difficulty>unlocked)return fail("tower floor is locked",ServerErrorCode.ERROR_NO_OPEN_FLOOR);
            if(p.ActiveStage!=null)return fail("another battle is active before tower entry",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(t.Areas.ContainsKey((uint)row.Id))return fail("tower area is already cleared today",ServerErrorCode.ERROR_ALREADY_CLEARED_CHALLENGE_STAGE);
            if(start.stageid>1&&!t.Areas.ContainsKey((uint)(row.Id-1)))return fail("clear the previous tower area first",ServerErrorCode.ERROR_NOT_ENOUGH_STEP_CONDITION);
            if(start.stageid==1&&t.UsedEntries>=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.CHALLENGE_TOWER_ENTER_DAILY_MAX_COUNT])return fail("daily floor challenge opportunity is used",ServerErrorCode.ERROR_INVALID_OVER_ENTER_COUNT);
            if(start.stageid>1&&t.DailyFloor!=start.difficulty)return fail("this tower floor has no active daily entry",ServerErrorCode.ERROR_NOT_ENOUGH_STEP_CONDITION);
            var cards=DeckSlots(p,25).Where(x=>x!=0).ToList();
            var banned=TowerBan(start.difficulty);
            if(cards.Count==0||cards.Count>5||cards.Distinct().Count()!=cards.Count)return fail("invalid tower starter roster size or duplicates",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(cards.Any(x=>!Owns(p,x)))return fail("missing tower starter card",ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE);
            if(cards.Any(x=>InTraining(p,x)))return fail("tower starter is in training",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            if(cards.Any(t.DeadCards.Contains))return fail("tower deck contains fallen card",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(cards.Any(x=>banned.Contains(catalog.RequireCard(p.CardIds[(int)x-1]).EvolveFirstAncestor)))return fail("tower deck contains a permanently or daily banned card",ServerErrorCode.ERROR_USED_BAN_CARD);
            if(!catalog.TowerWaves.Any(x=>x.Id==row.Id&&x.WaveList.Count>0))return fail("original tower enemies are missing",ServerErrorCode.ERROR_INVALID_STAGE_ID);
            t.ActiveId=(uint)row.Id;t.ActiveStamp=NextLocalBattleStamp(p);t.BattleCards=cards;
            LocalBattleAttempts.Bind(p,t.ActiveId,t.ActiveStamp);
            result=ok(new GsChallengeStageBattleStartOk{difficulty=start.difficulty,stageid=start.stageid,timeStamp=t.ActiveStamp,usedEnterCount=t.UsedEntries});
        } else {
            var end=(GsChallengeStageBattleEndConfirmRequest)request!;var row=catalog.TowerStages.FirstOrDefault(x=>x.TowerId==end.difficulty&&x.AreaId==end.stageid);
            var side=end.battleEndSyncData?.sideDeckCardSeq.ToList()??[];
            var roster=t.BattleCards.Concat(side).ToHashSet();var banned=row==null?new HashSet<int>():TowerBan(end.difficulty);
            if(row==null||t.ActiveId!=row.Id||end.timeStamp!=t.ActiveStamp||!LocalBattleAttempts.Matches(p,t.ActiveId,t.ActiveStamp)||!Enum.IsDefined(end.endType))return fail("no matching tower battle for settlement",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(side.Count>35||side.Distinct().Count()!=side.Count||side.Any(x=>!Owns(p,x)||InTraining(p,x)||t.DeadCards.Contains(x)||t.BattleCards.Contains(x))||
                end.dieCardSeq.Distinct().Count()!=end.dieCardSeq.Count||end.dieCardSeq.Any(x=>!roster.Contains(x)))return fail("invalid tower reserve or fallen card list",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            if(side.Any(x=>banned.Contains(catalog.RequireCard(p.CardIds[(int)x-1]).EvolveFirstAncestor)))return fail("tower reserve contains a permanently or daily banned card",ServerErrorCode.ERROR_USED_BAN_CARD);
            bool won=end.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS;
            // BattleStage.GetServerEnemyAliveCount adds (serverStamp % 1000)
            // multiplied by currentStage; tower scenes retain the original default 1.
            uint aliveOffset=(uint)(end.timeStamp%1000);
            int enemyCount=catalog.TowerWaves.Single(x=>x.Id==row.Id).WaveList.Sum(x=>x.MobCardInfoList.Count);
            if(end.cardDieCount!=end.dieCardSeq.Count||end.enemyAliveCount<aliveOffset||end.enemyAliveCount-aliveOffset>enemyCount||won&&end.enemyAliveCount!=aliveOffset||!won&&end.dieCardSeq.Count==0)return fail("inconsistent tower outcome",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            var dead=end.dieCardSeq.ToList();byte stars=(byte)(dead.Count==0?3:dead.Count==1?2:1);
            uint points=won?checked((uint)row.TowerPoint*(uint)catalog.Numbers[stars==3?BscTable.NUMBER_TYPE.CHALLENGE_TOWER_ACHIEVEMENT_POINT_03:stars==2?BscTable.NUMBER_TYPE.CHALLENGE_TOWER_ACHIEVEMENT_POINT_02:BscTable.NUMBER_TYPE.CHALLENGE_TOWER_ACHIEVEMENT_POINT_01]):0;
            var grant=new ShopGrant{Gold=won?(uint)row.Gold:0,Cash=won?(uint)row.Gem:0};
            if(won)for(int i=0;i<row.RewardId.Count;i++) {
                if(row.RewardIdType[i]!=BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM||i>=row.DropProb.Count||row.DropProb[i]!=100||!AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,row.RewardId[i],row.RewardQuantity[i]))return Invalid("unsupported original tower reward");
            }
            if((ulong)t.Points+points>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("tower reward storage capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var ended=new GsChallengeStageBattleEndConfirmOk{earnedCoinInfo=new(){gold=(uint)grant.Gold,cashPoint=(uint)grant.Cash},earnedChallengePoint=points,battleEndSyncData=new()};
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,ended.earnedItemList,[]);
            t.DeadCards.UnionWith(dead);t.Points+=points;
            if(won){t.Areas[(uint)row.Id]=stars;if(row.AreaId==1){t.UsedEntries++;t.DailyFloor=(uint)row.TowerId;}if(row.TowerId>t.BestFloor||row.TowerId==t.BestFloor&&row.AreaId>t.BestArea){t.BestFloor=(uint)row.TowerId;t.BestArea=(uint)row.AreaId;}}
            if (won) RecordObjective(p, BscTable.CHECK_TYPE.MCT_WIN_CHALLENGE_STAGE);
            ClearTowerAttempt(p);ended.coinInfo=Coins(p);ended.challengePoint=t.Points;result=ok(ended);
        }
        if(p.TowerReceipts.Count>=256)p.TowerReceipts.Remove(p.TowerReceipts.Keys.First());
        p.TowerReceipts[key]=new(){Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};store.Save();return result;
    }
}
