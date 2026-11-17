internal sealed partial class ProtocolRouter
{
    private PKGuildBattleEnemyUserInfo GuildBattleEnemy(LocalPlayer p,LocalCompetitiveOffer offer)
    {
        return new(){enemyGuildId=offer.Uid,enemyGuildLevel=MyGuild(p)!.Level,enemyGuildRank=offer.Rank,
            enemyGuildRankGrade=offer.Badge,enemyGuildNick=offer.Name,enemyGuildEmblemID=(uint)Guilds.Emblems[(int)(offer.Uid%(uint)Guilds.Emblems.Count)].Id};
    }
    private uint GuildBattleRank(LocalGuild g)=>1u+(uint)AllGuilds.Count(x=>!x.Disbanded&&x.Id!=g.Id&&(x.Battle.Points>g.Battle.Points||x.Battle.Points==g.Battle.Points&&x.Id<g.Id));
    private GsGetGuildBattleRecordOk GuildBattleRecord(LocalGuild g)
    {
        var b=g.Battle;var result=new GsGetGuildBattleRecordOk{guildBattleRecord=new(){rank=GuildBattleRank(g),currentBattleCount=b.Battles,currentBattleWinCount=b.Wins,currentBattlePoint=b.Points,bestBattleCount=b.BestBattles,bestBattleWinCount=b.BestWins,bestBattlePoint=b.BestPoints}};
        result.guildBattleHistoryList.AddRange(b.History.Take(10).Select(x=>(PKGuildBattleHistory)WireCodec.Deserialize(typeof(PKGuildBattleHistory),x)!));return result;
    }
    private LocalPlayer? GuildVolunteer(LocalPlayer p,uint uid,uint card)
    {
        var guild=MyGuild(p);var actor=GuildActor(uid);
        if(uid==p.Uid||guild==null||actor==null||!guild.Members.ContainsKey(uid)||p.Guild.BattleVolunteers.Contains(uid))return null;
        ulong seq=actor.Deck.FirstOrDefault(x=>x!=0);return seq!=0&&actor.CardIds[(int)seq-1]==card&&!InTraining(actor,seq)?actor:null;
    }
    private Reply? GuildBattleOperation(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool local=request is LocalRankCombatRequest{mode:29};
        bool reset=request is GsResetPvpBattleCoolTimeRequest cool&&(int)cool.pvpBattleType==6||request is GsResetPvpBattleEntranceCountRequest enter&&(int)enter.pvpBattleType==6;
        if(!local&&!reset&&name is not ("GS_GET_GUILD_BATTLE_RECORD_REQUEST" or "GS_SEARCH_GUILD_BATTLE_ENEMY_LIST_REQUEST" or "GS_GET_GUILD_BATTLE_ENEMY_DECK_INFO_REQUEST" or "GS_GET_GUILD_ARENA_BATTLE_HISTORY_REQUEST" or "GS_GET_GUILD_BATTLE_USED_VOLUNTEER_LIST_REQUEST" or "GS_GET_GUILD_BATTLE_VOLUNTEER_CARD_BATTLE_INFO_REQUEST" or "GS_UPDATE_GUILD_BATTLE_RESULT_REQUEST"))return null;
        Reply Bad(string why,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS)=>fail(why,code);
        if(request is LocalRankCombatRequest recovery&&recovery.operation is 0 or 3){var pending=p.Competitive;if(recovery.operation==3&&(recovery.stamp==0||pending.ActiveMode!=29||pending.Stamp!=recovery.stamp||p.ActiveBattleStamp!=recovery.stamp))return Bad("stale guild battle retreat");if(pending.ActiveMode==29)ClearCompetitive(p);RefreshCompetitive(p);store.Save();return ok(new LocalRankCombatReply{operation=recovery.operation,stamp=recovery.stamp,entrance=CompetitiveEntrance(p),coins=Coins(p)});}
        var g=MyGuild(p);if(g==null)return Bad("guild battle requires current guild membership",ServerErrorCode.ERROR_NOT_GUILD_USER);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_PVP_GUILD_ARENA))return Bad("original guild battle unlock level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        RefreshCompetitive(p);var r=PvpRecord(p,29);var c=p.Competitive;var level=Guilds.Levels.Single(x=>x.GuildLevel==g.Level);
        if(name=="GS_GET_GUILD_BATTLE_RECORD_REQUEST")return ok(GuildBattleRecord(g));
        if(name=="GS_GET_GUILD_BATTLE_USED_VOLUNTEER_LIST_REQUEST"){var list=new GsGetGuildBattleVolunteerListOk();list.usedUserUidList.AddRange(p.Guild.BattleVolunteers);return ok(list);}
        if(request is GsGetVolunteerCardDetailInfoRequest volunteer){var actor=GuildVolunteer(p,volunteer.volunteerUserUid,volunteer.volunteerCardID);return actor==null?Bad("guild volunteer unavailable or already used today",ServerErrorCode.ERROR_ALREADY_USE_GUILD_MERCENARY_CARD):ok(new GsGetVolunteerCardDetailInfoOk{guildUserBattleDeckCardInfo=RankingCard(actor,actor.Deck.First(x=>x!=0),0)});}
        if(name=="GS_SEARCH_GUILD_BATTLE_ENEMY_LIST_REQUEST"){
            var list=new GsSearchGuildBattleEnemyListOk{currentRank=GuildBattleRank(g)};
            list.enemyList.AddRange(CompetitiveOffers(p,29).Select(x=>GuildBattleEnemy(p,x)));store.Save();return ok(list);
        }
        if(name=="GS_GET_GUILD_ARENA_BATTLE_HISTORY_REQUEST"){
            var list=new GsGetGuildArenaBattleHistoryOk();list.arenaBattleHistoryList.AddRange(g.Battle.ArenaHistory.Take(30).Select(x=>(PKGuildArenaBattleHistoryData)WireCodec.Deserialize(typeof(PKGuildArenaBattleHistoryData),x)!));return ok(list);
        }
        if(request is GsGetGuildBattleEnemyDeckInfoRequest preview){
            var offer=CompetitiveOffers(p,29).FirstOrDefault(x=>x.Uid==preview.targetGuildId);if(offer==null)return Bad("guild battle offer changed; refresh opponents",ServerErrorCode.ERROR_INVALID_GUILD_BATTLE_RANK_INFO);
            return ok(GuildEnemyDeck(CompetitiveProfile(offer)));
        }
        if(reset){
            if(p.ActiveStage!=null)return Bad("finish active battle before a guild battle reset");
            bool cooldown=request is GsResetPvpBattleCoolTimeRequest;
            if(cooldown?(r.Latest==0||Now>=r.Latest+(ulong)catalog.Numbers[BscTable.NUMBER_TYPE.GUILD_BATLE_COOLTIME_SEC]):(r.Used<level.MaxBattleCount||r.Bought>=level.ResetBattleCount))return Bad("guild battle reset unavailable",ServerErrorCode.ERROR_INVALID_PVP_RESET_ENTRANCE_COUNT);
            var fee=catalog.Expenses.FindItem(cooldown?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_GUILD_ARENA_COOLTIME:BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_GUILD_ARENA_ENTER,checked((int)(cooldown?r.CoolResets:r.Bought)+1),true);
            if(fee==null||fee.Value<0||!CanPayAndGrant(p,new(),fee.MoneyType,(uint)fee.Value))return Bad("guild battle reset currency insufficient",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            ApplyShopGrant(p,new(),fee.MoneyType,(uint)fee.Value,[],[]);if(cooldown){r.Latest=0;r.CoolResets++;return ok(new GsResetPvpBattleCoolTimeOk{coinInfo=Coins(p)});}r.Used=0;r.Bought++;return ok(new GsResetPvpBattleEntranceCountOk{coinInfo=Coins(p)});
        }
        if(request is not LocalRankCombatRequest battle)return Bad("update the client and server together for token-bound guild battles");
        if(battle.operation==0){if(c.ActiveMode==29)ClearCompetitive(p);store.Save();return ok(new LocalRankCombatReply{entrance=CompetitiveEntrance(p),coins=Coins(p)});}
        if(battle.operation==3){if(battle.stamp==0||c.ActiveMode!=29||c.Stamp!=battle.stamp||p.ActiveBattleStamp!=battle.stamp)return Bad("stale guild battle retreat");ClearCompetitive(p);return ok(new LocalRankCombatReply{operation=3,stamp=battle.stamp,entrance=CompetitiveEntrance(p),coins=Coins(p)});}
        if(battle.operation==1){
            if(p.ActiveStage!=null)return Bad("finish active battle before starting guild battle");
            if(r.Used>=level.MaxBattleCount)return Bad("guild battle entries exhausted",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(r.Latest!=0&&Now<r.Latest+(ulong)catalog.Numbers[BscTable.NUMBER_TYPE.GUILD_BATLE_COOLTIME_SEC])return Bad("guild battle cooldown; wait or reset",ServerErrorCode.ERROR_INVALID_PVP_RESET_ENTRANCE_COUNT);
            if(battle.cards.Count is <1 or >15||battle.cards.Skip(5).Any(x=>x!=0))return Bad("guild battle requires five native slots with optional empty padding",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            var merc=battle.guildMercenary;var own=battle.cards.Take(5).Concat(new ulong[5]).Take(5).ToList();LocalPlayer? volunteerActor=null;
            if(merc!=null){
                volunteerActor=GuildVolunteer(p,merc.uid,merc.cardId);if(merc.slotIndex>=5||volunteerActor==null)return Bad("guild volunteer unavailable or already used today",ServerErrorCode.ERROR_ALREADY_USE_GUILD_MERCENARY_CARD);
                own[(int)merc.slotIndex]=0;
            }
            if(!CompetitiveRoster(p,own,5,out var cards)&&!(volunteerActor!=null&&own.All(x=>x==0)))return Bad("guild battle requires distinct available owned cards",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            var offer=CompetitiveOffers(p,29).FirstOrDefault(x=>x.Uid==battle.targetUid);if(offer==null)return Bad("guild battle offer changed; refresh opponents",ServerErrorCode.ERROR_INVALID_GUILD_BATTLE_RANK_INFO);
            var enemy=CompetitiveProfile(offer);BeginCompetitive(p,29,enemy,cards);c.GuildId=g.Id;c.Spot=offer.Uid;c.Action=offer.Badge;c.Started=true;c.GuildMercenaryUid=merc?.uid??0;
            for(int i=0;i<5;i++){if(merc!=null&&merc.slotIndex==i)c.GuildReplayCards.Add(merc.cardId);else if(own[i]!=0)c.GuildReplayCards.Add(p.CardIds[(int)own[i]-1]);}
            p.Decks[50]=own;LocalBattleAttempts.Bind(p,0xF0000000u+29,c.Stamp);r.Used++;r.Latest=Now;ConsumeBsBalls(p,cards);
            if(merc!=null)p.Guild.BattleVolunteers.Add(merc.uid);
            var reply=new LocalRankCombatReply{operation=1,stamp=c.Stamp,guildEnemy=GuildEnemyDeck(enemy),entrance=CompetitiveEntrance(p),coins=Coins(p)};
            if(volunteerActor!=null){var snapshot=RankingCard(volunteerActor,volunteerActor.Deck.First(x=>x!=0),merc!.slotIndex);reply.enemyCards.Add(snapshot);}
            return ok(reply);
        }
        if(battle.operation==2){
            if(c.ActiveMode!=29||c.Stamp!=battle.stamp||p.ActiveBattleStamp!=battle.stamp||!c.Started||c.GuildId!=g.Id||c.Spot!=battle.targetUid||battle.result>1||c.ReplayWinner!=battle.result||!battle.cards.Where(OwnsCard).SequenceEqual(c.Cards))return Bad("guild battle settlement does not match active token, roster, membership or replay",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            bool OwnsCard(ulong seq)=>seq!=0&&Owns(p,seq);
            var offer=CompetitiveOffers(p,29).Single(x=>x.Uid==c.Spot);bool won=battle.result==0;var b=g.Battle;var rewardType=won?(c.Action==0?BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_STRONG_WIN:c.Action==1?BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_NORMAL_WIN:BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_WEAK_WIN):BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_LOSE;
            var row=Competitive.Rewards.FirstOrDefault(x=>x.PvpBattleType==BscTable.PVP_BATTLE_TYPE.PVP_BATTLE_GUILDARENA&&x.BattleResultType==rewardType);
            if(row==null)return Bad("original guild battle reward row missing",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            var grant=new ShopGrant();uint guildPoints=0;ulong arena=0,rank=0;
            for(int i=0;i<row.ItemQuantity.Count;i++){if(row.RewardValueIdType[i]==BscTable.REWARD_TYPE.REWARD_TYPE_GUILD_POINT)guildPoints=checked(guildPoints+(uint)row.ItemQuantity[i]);else if(row.RewardValueIdType[i]==BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT)arena+=(uint)row.ItemQuantity[i];else if(row.RewardValueIdType[i]==BscTable.REWARD_TYPE.REWARD_TYPE_RANK_POINT)rank+=(uint)row.ItemQuantity[i];else if(!AddShopReward(p,grant,row.RewardValueIdType[i],row.RewardValueId[i],row.ItemQuantity[i]))return Bad("guild battle reward invalid",ServerErrorCode.ERROR_OVER_MAX_COUNT);}
            if((ulong)p.ArenaPoint+arena>uint.MaxValue||(ulong)p.RankingPoint+rank>uint.MaxValue||(ulong)g.Points+guildPoints>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return Bad("guild battle reward capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,[],[]);g.Points+=guildPoints;p.ArenaPoint+=(uint)arena;p.RankingPoint+=(uint)rank;
            int delta=(int)catalog.Numbers[won?(c.Action==0?BscTable.NUMBER_TYPE.REWARD_POINT_BATTLE_RESULT_STRONG_WIN:c.Action==1?BscTable.NUMBER_TYPE.REWARD_POINT_BATTLE_RESULT_NORMAL_WIN:BscTable.NUMBER_TYPE.REWARD_POINT_BATTLE_RESULT_WEAK_WIN):BscTable.NUMBER_TYPE.REWARD_POINT_BATTLE_RESULT_LOSE];
            b.Battles++;if(won)b.Wins++;b.Points=(uint)Math.Clamp((long)b.Points+delta,0,uint.MaxValue);b.BestBattles=Math.Max(b.BestBattles,b.Battles);b.BestWins=Math.Max(b.BestWins,b.Wins);b.BestPoints=Math.Max(b.BestPoints,b.Points);
            r.Battles++;if(won)r.Wins++;r.LossStreak=won?0:r.LossStreak+1;
            b.History.Insert(0,WireCodec.Serialize(new PKGuildBattleHistory{nickName=p.Nickname,deltaRank=(uint)Math.Abs(delta),timeStamp=Now,battleResult=battle.result}));
            var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),c.Enemy)!;
            b.ArenaHistory.Insert(0,WireCodec.Serialize(new PKGuildArenaBattleHistoryData{enemyGuildName=offer.Name,arenaHistoryData=new(){battleTime=Now,battleResult=battle.result,enemyUid=c.Opponent,enemyCommonInfo=enemy.commonInfo,enemyCardId=enemy.leaderCardId,enemyCardLevel=enemy.leaderCardLevel,replaySeq=c.Stamp}}));
            if(b.History.Count>30)b.History.RemoveAt(30);if(b.ArenaHistory.Count>30)b.ArenaHistory.RemoveAt(30);
            RecordCompetitiveObjective(p,29,won);
            var result=new GsUpdateGuildBattleResultOk{currentRank=GuildBattleRank(g),battleEndSyncData=BallSync(p)};
            for(int i=0;i<row.ItemQuantity.Count;i++)result.rewardItemInfoList.Add(new(){rewardItemType=row.RewardValueIdType[i],rewardItemData=(ulong)row.RewardValueId[i],rewardItemCount=(uint)row.ItemQuantity[i]});
            p.Competitive.Offers.Remove(29);ClearCompetitive(p);return ok(new LocalRankCombatReply{operation=2,stamp=battle.stamp,guildResult=result,entrance=CompetitiveEntrance(p)});
        }
        return Bad("unknown guild battle operation");
    }
    private GsGetGuildBattleEnemyDeckInfoOk GuildEnemyDeck(LocalPlayer enemy)
    {
        var result=new GsGetGuildBattleEnemyDeckInfoOk{targetUserInfo=GuildThumbnail(enemy),formationInfo=new(){id=1,level=1}};
        result.battleCardInfoList.AddRange(enemy.Deck.Select((seq,i)=>RankingCard(enemy,seq,(uint)i)));return result;
    }
}
