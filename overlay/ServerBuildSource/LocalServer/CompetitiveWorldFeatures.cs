internal sealed partial class ProtocolRouter
{
    private ulong WorldDayOffset => (32-(ulong)catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_RESET_TIME])*3600;
    private ulong WorldDay => (Now+WorldDayOffset)/86400;
    private static ulong WorldWeek(ulong day)=>(day+3)/7;
    private static (ulong Day,ulong Week) WorldPeriods(LocalCompetitiveState state)
    {
        if(state.WorldDay!=0)return (state.WorldDay,state.WorldWeek);
        return (state.Day, state.Week==state.Day/7?WorldWeek(state.Day):0);
    }
    private LocalWorldScore WorldScoreForBattle(LocalPlayer p,uint map,ulong day)
    {
        var state=p.Competitive;
        var periods=WorldPeriods(state);
        foreach(var score in state.WorldScores.Values)
        {
            if(periods.Day<day)score.Attack=score.Defence=score.Daily=0;
            if(periods.Week<WorldWeek(day))score.Weekly=0;
        }
        state.WorldDay=Math.Max(periods.Day,day);
        state.WorldWeek=Math.Max(periods.Week,WorldWeek(day));
        if(!state.WorldScores.TryGetValue(map,out var current))state.WorldScores[map]=current=new();
        return current;
    }
    private PKWorldWarBattleUserInfo WorldUser(LocalPlayer p,uint map,uint score,uint rank)
    {
        var card=NameCard(p);
        return new(){uid=p.Uid,serverId=1,territoryId=map,totalPoint=score,rank=rank,commonInfo=card.commonInfo,leaderCardInfo=card.leaderCardInfo,registerTime=Now};
    }
    private List<(LocalPlayer Player,uint Score)> WorldRankers(uint map,bool weekly,ulong day)
    {
        return store.All().Select(p=>{
            var periods=WorldPeriods(p.Competitive);
            var score=p.Competitive.WorldScores.GetValueOrDefault(map);
            uint points=weekly?(periods.Week==WorldWeek(day)?score?.Weekly??0:0):(periods.Day==day?score?.Daily??0:0);
            return (Player:p,Score:points);
        }).Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.Player.Uid).ToList();
    }
    private uint WorldRewardGroup(uint map,bool weekly)
    {
        var row=Competitive.Territories.Single(x=>x.Id==map);int fixedGroup=weekly?row.FixingWeekRewardGropId:row.FixingDayRewardGropId;
        if(fixedGroup>0)return (uint)fixedGroup;var groups=Competitive.WorldRewards.Where(x=>x.UseGroup&&(int)x.GroupType==(weekly?1:0)).OrderBy(x=>x.GroupId).ToArray();return groups.Length==0?0:(uint)groups[(map-1)%groups.Length].GroupId;
    }
    private PKWorldWarTerritoryInfo WorldTerritory(LocalPlayer p,uint map)
    {
        var result=new PKWorldWarTerritoryInfo{id=map,dailyRewardGroupId=WorldRewardGroup(map,false),weeklyRewardGroupId=WorldRewardGroup(map,true)};
        // The lobby consumes only each territory's champion. The complete board
        // has a separate request; duplicating full podiums exceeds the 8-KiB frame.
        ulong day=WorldDay;
        foreach(var row in WorldRankers(map,false,day).Take(1))result.dailyRankerList.Add(WorldUser(row.Player,map,row.Score,1));
        foreach(var row in WorldRankers(map,true,day).Take(1))result.weeklyRankerList.Add(WorldUser(row.Player,map,row.Score,1));
        if(p.Competitive.Wanted.TryGetValue(map,out uint uid)&&CompetitiveOpponents(p,44).FirstOrDefault(x=>x.Uid==uid) is {} target)result.wantedUserList.Add(WorldUser(target,map,0,0));return result;
    }
    private PKWorldWarRankInfo WorldRank(LocalPlayer p,uint map)
    {
        ulong day=WorldDay;
        var periods=WorldPeriods(p.Competitive);
        var s=p.Competitive.WorldScores.GetValueOrDefault(map);
        var daily=WorldRankers(map,false,day);
        var weekly=WorldRankers(map,true,day);
        return new(){uid=p.Uid,territoryId=map,attackPoint=periods.Day==day?s?.Attack??0:0,defencePoint=periods.Day==day?s?.Defence??0:0,
            dailyTotalPoint=periods.Day==day?s?.Daily??0:0,weeklyTotalPoint=periods.Week==WorldWeek(day)?s?.Weekly??0:0,
            dailyRank=(uint)(daily.FindIndex(x=>x.Player.Uid==p.Uid)+1),weeklyRank=(uint)(weekly.FindIndex(x=>x.Player.Uid==p.Uid)+1)};
    }
    private PKWorldWarUserInfo WorldUserInfo(LocalPlayer p)=>new(){uid=p.Uid,revivalRemainCount=(uint)Math.Max(0,catalog.VipFor(p.VipExp).WorldWarMaxRecoveryCount+p.Competitive.WorldReviveBought-p.Competitive.WorldRevives),revivalBuyCount=p.Competitive.WorldReviveBought};
    private PKSavedBattleCardInfo WorldCard(LocalPlayer p,ulong seq)
    {
        var card=p.Competitive.WorldCards.GetValueOrDefault(seq)??new();return new(){cardSeq=seq,hpRatio=card.Hp,spRatio=card.Sp,spBubbleCount=card.Bubbles};
    }
    private bool WorldOpen()=>!InLocalHourWindow(catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_ON],catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_OFF]);
    private static bool InLocalHourWindow(double from,double to){double hour=((Now+8*3600)%86400)/3600d;return from<=to?hour>=from&&hour<to:hour>=from||hour<to;}
    private Reply? HandleCompetitiveWorld(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!name.Contains("WORLD_WAR"))return null;Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);var c=p.Competitive;
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_WORLDWAR)||p.CardIds.Count(x=>x!=0)<catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_MINIMUM_VOLUME])return fail("world war requires its original level and collected-card count",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(request is GsGetWorldWarLobbyInfoRequest) {
            if(c.ActiveMode==44)ClearCompetitive(p);var result=new GsGetWorldWarLobbyInfoOk();result.territoryInfoList.AddRange(Competitive.Territories.Where(x=>x.MapOpen).Select(x=>WorldTerritory(p,(uint)x.Id)));return ok(result);
        }
        uint territory=(uint?)Field(request,"territoryId")??c.Map;
        var map=Competitive.Territories.FirstOrDefault(x=>x.Id==territory&&x.MapOpen);
        if(request is GsUpdateWorldWarDefenceDeckInfoRequest set) {
            var decks=set.userDeckList;
            if(decks.Count!=3||!decks.Select(x=>(uint)x.deckID).Order().SequenceEqual(new[]{34u,35u,36u}))return Bad("world war defence needs its three native decks");
            var all=decks.SelectMany(x=>new[]{x.cardSlot1,x.cardSlot2,x.cardSlot3,x.cardSlot4,x.cardSlot5}).ToList();
            if(!CompetitiveRoster(p,all,15,out var cards)||cards.Count!=15)return Bad("world war defence needs fifteen distinct owned available cards");
            foreach(var deck in decks)p.Decks[(uint)deck.deckID]=new(){deck.cardSlot1,deck.cardSlot2,deck.cardSlot3,deck.cardSlot4,deck.cardSlot5};return ok(null);
        }
        if(request is GsGetWorldWarTerritoryInfoRequest) {
            if(map==null)return Bad("unknown territory");return ok(new GsGetWorldWarTerritoryInfoOk{territoryInfo=WorldTerritory(p,territory),rankInfo=WorldRank(p,territory),latestRenewMinute=(uint)((Now/60)%60),latestRenewHour=(uint)((Now/3600+8)%24)});
        }
        if(request is GsGetWorldWarRankerInfoListRequest ranks) {
            if(map==null||!Enum.IsDefined(ranks.rankType))return Bad("unknown world-war ranking");
            bool weekly=ranks.rankType==WorldWarRankType.WEEKLY;
            var rows=WorldRankers(territory,weekly,WorldDay);
            int mine=rows.FindIndex(x=>x.Player.Uid==p.Uid);
            var result=new GsGetWorldWarRankerInfoListOk{myRankInfo=WorldUser(p,territory,mine<0?0:rows[mine].Score,(uint)(mine+1)),rewardGroupId=WorldRewardGroup(territory,weekly),latestRenewHour=(uint)((Now/3600+8)%24)};
            for(int i=0;i<Math.Min(100,rows.Count);i++)
            {
                result.rankerInfoList.Add(WorldUser(rows[i].Player,territory,rows[i].Score,(uint)i+1));
                if(WireCodec.Serialize(result).Length+WireCodec.HeaderSize<=WireCodec.MaxPacketSize)continue;
                result.rankerInfoList.RemoveAt(i);
                break;
            }
            return ok(result);
        }
        if(request is GsGetWorldWarWantedUserInfoRequest) {
            if(map==null)return Bad("unknown territory");var result=new GsGetWorldWarWantedUserInfoOk{latestRenewHour=(uint)((Now/3600+8)%24),latestRenewMinute=(uint)((Now/60)%60)};result.wantedUserInfoList.AddRange(WorldTerritory(p,territory).wantedUserList);result.battledWantedUserUidList.AddRange(c.WantedBattled);return ok(result);
        }
        if(request is GsUpdateWorldWarWantedUserInfoRequest wanted) {
            if(map==null||wanted.wantedUserUid==p.Uid||!CompetitiveOpponents(p,44).Any(x=>x.Uid==wanted.wantedUserUid))return Bad("invalid wanted opponent");uint cost=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_WANTED_CASH]);
            if(p.CashPoint<cost)return fail("not enough wanted diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);p.CashPoint-=cost;c.Wanted[territory]=wanted.wantedUserUid;return ok(new GsUpdateWorldWarWantedUserInfoOk{coinInfo=Coins(p)});
        }
        if(request is GsWorldWarReadyRequest ready) {
            if(map==null)return fail("unknown territory",ServerErrorCode.ERROR_INVALID_TERRITORY_ID);
            if(p.Level<map.LevelMin||p.Level>map.LevelMax)return fail("territory level range",ServerErrorCode.ERROR_INVALID_TERRITORY_ENTRANCE_LEVEL);
            if(!WorldOpen())return fail("world war is in its daily break",ServerErrorCode.ERROR_WORLD_WAR_BREAK_TIME);
            if(p.ActiveStage!=null||c.ActiveMode is not (0 or 44))return fail("finish the current battle first",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            var defence=Enumerable.Range(34,3).SelectMany(x=>DeckSlots(p,(uint)x)).ToArray();if(!CompetitiveRoster(p,defence,15,out var defenders)||defenders.Count!=15)return fail("configure fifteen distinct world-war defenders first",ServerErrorCode.ERROR_NOT_REGISTER_DEFENCE_DECK);
            // Native Ready opens the editor before its first attack roster exists.
            // Preserve fatigue in the preview; admission validates the saved roster.
            var cards=DeckSlots(p,33).Where(seq=>seq!=0&&Owns(p,seq)).Distinct().Take(5).ToList();
            var enemies=CompetitiveOpponents(p,44);var enemy=ready.enemyUid==0?AutomaticCompetitiveOpponent(p,44,c.WorldDifficulty.Battles):enemies.FirstOrDefault(x=>x.Uid==ready.enemyUid);if(enemy==null)return fail("unknown world-war opponent",ServerErrorCode.ERROR_NOT_EXIST_PVP_AI_DECK);
            if(c.WantedBattled.Contains(enemy.Uid)&&c.Wanted.GetValueOrDefault(territory)==enemy.Uid)return fail("wanted opponent already challenged today",ServerErrorCode.ERROR_ALREADY_BATTLED_WITH_WANTED_USER);
            BeginCompetitive(p,44,enemy,cards,territory);var info=CompetitiveEnemy(enemy);var result=new GsWorldWarReadyOk{userInfo=WorldUserInfo(p),enemyInfo=new(){battleUserInfo=WorldUser(enemy,territory,0,0),formation=info.formationInfo,bAIBattle=true}};
            result.enemyInfo.cardinfolist.AddRange(info.cardInfoList);result.myCardStatusList.AddRange(cards.Select(x=>WorldCard(p,x)));return ok(result);
        }
        if(request is GsWorldWarStartRequest) {
            if(c.ActiveMode!=44||c.Opponent==0||c.Started||p.ActiveStage!=null)return fail("world-war ready state expired",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(!WorldOpen())return fail("world war is in its daily break",ServerErrorCode.ERROR_WORLD_WAR_BREAK_TIME);
            var defence=Enumerable.Range(34,3).SelectMany(x=>DeckSlots(p,(uint)x));
            if(!CompetitiveRoster(p,defence,15,out var defenders)||defenders.Count!=15)return fail("configure fifteen distinct world-war defenders first",ServerErrorCode.ERROR_NOT_REGISTER_DEFENCE_DECK);
            var slots=DeckSlots(p,33);
            if(slots.Any(seq=>seq!=0&&Owns(p,seq)&&InTraining(p,seq)))return fail("world-war attack deck contains training cards",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            if(!CompetitiveRoster(p,slots,5,out var cards)||cards.Count!=5)return fail("world-war attack deck needs five distinct available cards",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(cards.Any(seq=>WorldCard(p,seq).hpRatio<=0))return fail("world-war attack deck contains dead cards",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(!cards.SequenceEqual(c.Cards))return fail("world-war prepared roster changed; reopen the editor",ServerErrorCode.ERROR_INVALID_BATTLE_STATUS_FLAG);
            c.Started=true;LocalBattleAttempts.Bind(p,0xF0000000u+44,c.Stamp);ConsumeBsBalls(p,c.Cards);return ok(null);
        }
        if(request is GsWorldWarEndConfirmRequest end) {
            if(c.ActiveMode!=44||!c.Started||p.ActiveBattleStamp!=c.Stamp)return fail("world-war result has no matching battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(c.ReplayWinner!=(end.bWin?0u:1u))return fail("world-war result has no matching completed replay",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            if(end.myCardStatusList.Count!=c.Cards.Count||end.myCardStatusList.Select(x=>x.cardSeq).Distinct().Count()!=end.myCardStatusList.Count||end.myCardStatusList.Any(x=>!c.Cards.Contains(x.cardSeq)))return fail("world-war survivor roster differs from the started battle",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            foreach(var status in end.myCardStatusList){
                var nativeCard=catalog.RequireCard(p.CardIds[(int)status.cardSeq-1]);
                // BattleStage.GetSavedCardList sends SpiritPoint here. Old saved
                // ratios <= 1 are still accepted by native GetSpMigration.
                int maxSp=nativeCard.MaxSp>0?nativeCard.MaxSp:100;
                if(!float.IsFinite(status.hpRatio)||status.hpRatio<0||status.hpRatio>1||!float.IsFinite(status.spRatio)||status.spRatio<0||status.spRatio>maxSp||status.spBubbleCount>100)
                    return Bad($"world-war survivor stats; seq={status.cardSeq}; hp={status.hpRatio}; sp={status.spRatio}; maxSp={maxSp}; bubbles={status.spBubbleCount}");
            }
            foreach(var status in end.myCardStatusList)c.WorldCards[status.cardSeq]=new(){Hp=status.hpRatio,Sp=status.spRatio,Bubbles=status.spBubbleCount};
            bool isWanted=c.Wanted.GetValueOrDefault(c.Map)==c.Opponent;bool perfect=end.bWin&&end.myCardStatusList.Count==c.Cards.Count&&end.myCardStatusList.All(x=>x.hpRatio>0);var kind=isWanted?(end.bWin?(perfect?BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_WANTED_PERFECT_WIN:BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_WANTED_WIN):BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_WANTED_LOSE):(end.bWin?(perfect?BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_ATTACK_PERFECT_WIN:BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_ATTACK_WIN):BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_ATTACK_LOSE);
            uint points=(uint)Math.Max(0,catalog.Numbers[kind]);var score=WorldScoreForBattle(p,c.Map,WorldDay);score.Attack+=points;score.Daily+=points;score.Weekly+=points;c.WorldDifficulty.Record(end.bWin);if(isWanted)c.WantedBattled.Add(c.Opponent);
            var result=new GsWorldWarEndConfirmOk{rankInfo=WorldRank(p,c.Map),attackPoint=points,battleEndSyncData=BallSync(p)};result.myCardStatusList.AddRange(c.Cards.Select(x=>WorldCard(p,x)));c.Offers.Remove(44);ClearCompetitive(p);return ok(result);
        }
        if(request is GsReviveWorldWarCardRequest) {
            if(p.ActiveStage!=null)return fail("finish the current battle first",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(WorldUserInfo(p).revivalRemainCount==0)return fail("no world-war recovery remaining",ServerErrorCode.ERROR_NOT_ENOUGH_REVIVAL_COUNT);
            c.WorldCards.Clear();c.WorldRevives++;return ok(new GsReviveWorldWarCardOk{userInfo=WorldUserInfo(p)});
        }
        return Bad("unsupported world-war operation");
    }
}
