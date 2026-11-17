extern alias game;
using RankType = game::BscTable.RANKING_BOARD_TYPE;

internal sealed partial class ProtocolRouter
{
    private void InitRanking(LocalPlayer p)
    {
        var r=p.Ranking;
        if(!r.Initialized){
            r.EngravingTotal=p.EquipmentItems.Aggregate(0UL,(sum,x)=>sum+x.CurrentEmpowerValue);
            var regular=Raids.Schedules.Where(x=>!x.ComboMode).Select(x=>(uint)x.BossId).ToHashSet();
            var combo=Raids.Schedules.Where(x=>x.ComboMode).Select(x=>(uint)x.BossId).ToHashSet();
            r.WorldBest=p.Raids.BestDamage.Where(x=>regular.Contains(x.Key)&&!combo.Contains(x.Key)).Select(x=>x.Value).DefaultIfEmpty(0u).Max();
            r.ComboBest=p.Raids.BestDamage.Where(x=>combo.Contains(x.Key)&&!regular.Contains(x.Key)).Select(x=>x.Value).DefaultIfEmpty(0u).Max();
            r.Initialized=true;
        }
        ulong day=(Now+8*3600)/86400;if(r.LikeDay!=day){r.LikeDay=day;r.Liked.Clear();r.ReceivedToday=0;}
        if(r.ChargeDay!=TowerDay(Now)){r.ChargeDay=TowerDay(Now);r.ChargedToday=0;}
    }
    private static ulong GuildDonations(LocalGuild g)=>g.TotalDonationPoints??=g.Members.Values.Aggregate(0UL,(sum,x)=>sum+x.Contribution);
    private static ulong RankingScore(LocalPlayer p,RankType type)=>type switch {
        RankType.RANKING_BOARD_BATTLE_ABILITY=>(ulong)p.Ranking.TigerPower+p.Ranking.DragonPower+p.Ranking.SnakePower,
        RankType.RANKING_BOARD_BATTLE_ABILITY_WHITE_TIGER=>p.Ranking.TigerPower,
        RankType.RANKING_BOARD_BATTLE_ABILITY_BLUE_DRAGON=>p.Ranking.DragonPower,
        RankType.RANKING_BOARD_BATTLE_ABILITY_RED_SNAKE=>p.Ranking.SnakePower,
        RankType.RANKING_BOARD_ARENA_BATTLE_REAL_TIME=>p.Ranking.ArenaPower,
        RankType.RANKING_BOARD_INFINITE_ARENA_BATTLE_REAL_TIME=>p.Ranking.InfinitePower,
        RankType.RANKING_BOARD_BUY_CASH=>p.Ranking.ChargedTotal,
        RankType.RANKING_BOARD_BUY_CASH_TODAY=>p.Ranking.ChargedToday,
        RankType.RANKING_BOARD_EMPOWER=>p.Ranking.EngravingTotal,
        RankType.RANKING_BOARD_WORLD_BOSS=>p.Ranking.WorldBest,
        RankType.RANKING_BOARD_WORLD_BOSS_COMBO=>p.Ranking.ComboBest,
        RankType.RANKING_BOARD_BIG_BOSS_DAMAGE=>p.Ranking.BigBossDamage,
        RankType.RANKING_BOARD_WANDER_BATTLE_POINT=>p.Wander.BestPoints,
        RankType.RANKING_BOARD_CHAIN_BATTLE=>p.ChainBattle.BestWins,
        _=>0
    };
    private List<(LocalPlayer Player,ulong Score)> RankedPlayers(RankType type)
    {
        var rows=new List<(LocalPlayer Player,ulong Score)>();
        foreach(var p in store.All()){
            InitRanking(p);bool power=type is RankType.RANKING_BOARD_BATTLE_ABILITY or RankType.RANKING_BOARD_BATTLE_ABILITY_WHITE_TIGER or RankType.RANKING_BOARD_BATTLE_ABILITY_BLUE_DRAGON or RankType.RANKING_BOARD_BATTLE_ABILITY_RED_SNAKE;
            ulong value=RankingScore(p,type);if(value>0||power&&p.Ranking.HasPower)rows.Add((p,value));
        }
        return rows.OrderByDescending(x=>x.Score).ThenBy(x=>x.Player.Uid).ToList();
    }
    private List<(LocalGuild Guild,ulong Score)> RankedGuilds(bool battle=false)=>AllGuilds.Where(x=>!x.Disbanded).Select(x=>(Guild:x,Score:battle?(ulong)x.Battle.Points:GuildDonations(x))).Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.Guild.Id).ToList();
    private PKRankingBoardUserRankData RankingFor(LocalPlayer p,RankType type)
    {
        if(type is RankType.RANKING_BOARD_GUILD_CONTRIBUTION or RankType.RANKING_BOARD_GUILD_BATTLE_REALTIME){var guild=MyGuild(p);var rows=RankedGuilds(type==RankType.RANKING_BOARD_GUILD_BATTLE_REALTIME);int index=rows.FindIndex(x=>x.Guild.Id==guild?.Id);return new(){ranking=index<0?0u:(uint)index+1,rankingData=index<0?0:rows[index].Score};}
        var players=RankedPlayers(type);int i=players.FindIndex(x=>x.Player.Uid==p.Uid);return new(){ranking=i<0?0u:(uint)i+1,rankingData=i<0?0:players[i].Score};
    }
    private PKRankingBoardTopRankerUserData RankingUser(LocalPlayer p)=>new(){uid=p.Uid,nick=p.Nickname,level=p.Level,vipGrade=(uint)catalog.VipFor(p.VipExp).VipLv,titleId=SelectedTitle(p),account=p.Account};
    private PKGuildInfo RankingGuild(LocalPlayer p)=>MyGuild(p) is {} g?GuildInfo(g):new(){guildName="",guildLevel=1};
    private GsGetRankingBoardTopRankerOk RankingBoard(LocalPlayer p,RankType type)
    {
        var r=new GsGetRankingBoardTopRankerOk{userRankData=RankingFor(p,type)};
        if(type is RankType.RANKING_BOARD_GUILD_CONTRIBUTION or RankType.RANKING_BOARD_GUILD_BATTLE_REALTIME){
            uint position=0;foreach(var (g,score) in RankedGuilds(type==RankType.RANKING_BOARD_GUILD_BATTLE_REALTIME).Take(100))r.topRankerList.Add(new(){ranking=++position,rankingData=score,rankGuildInfo=new(){guildID=g.Id,guildName=g.Name,guildLevel=g.Level,guildPoint=g.Points,emblemID=g.Emblem,borderID=g.Border}});
        }else{
            uint position=0;foreach(var (q,score) in RankedPlayers(type).Take(100))r.topRankerList.Add(new(){ranking=++position,rankingData=score,rankUserInfo=RankingUser(q)});
        }
        r.currentCount=r.totalCount=(uint)r.topRankerList.Count;return r;
    }
    private PKBattleDeckCardInfo RankingCard(LocalPlayer p,ulong seq,uint slot)
    {
        var state=CardState(p,seq);var card=new PKBattleDeckCardInfo{uid=p.Uid,cardSeq=seq,cardId=p.CardIds[(int)seq-1],cardLevel=state.Level,enchantGrade=state.Enchant,cardSkinId=state.Skin,slotIndex=slot};
        card.cardSkillLevel.AddRange(state.ActiveSkills.Concat(state.PassiveSkills));card.cardSkillEnchant.AddRange(SkillEnchantSlots(state));
        foreach(ulong equipment in new[]{state.WeaponSequence}.Concat(state.AccessorySequences)){
            var owned=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==equipment);if(owned==null)continue;var item=new PKBattleDeckCardEquipInfo{itemID=owned.ItemId};item.empowerEffect.AddRange(owned.EmpowerValues);card.cardEquipInfo.Add(item);
        }
        card.cardEquipInfo.AddRange(state.GemIds.Where(x=>x!=0).Select(x=>new PKBattleDeckCardEquipInfo{itemID=x}));
        card.effectDataInfoList.AddRange(state.TranscendenceEffects.Select(x=>new PKEffectDataInfo{effectDataId=x.Id,effectDataValue=x.Value,effectGrade=x.Grade}));return card;
    }
    private GsGetLikeYouInfoOk RankingLikes(LocalPlayer p)
    {
        InitRanking(p);var r=new GsGetLikeYouInfoOk{remainLikeYouPoint=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.DAILY_LIKE_SEND_COUNT]-p.Ranking.Liked.Count)};r.likeyouSendTargetUidList.AddRange(p.Ranking.Liked.Order());return r;
    }
    private GsGetWorldBossTopRankerOk RankingWorldBoss(LocalPlayer p,uint id)
    {
        var all=store.All().Where(x=>x.Raids.BestDamage.GetValueOrDefault(id)>0).OrderByDescending(x=>x.Raids.BestDamage[id]).ThenBy(x=>x.Uid).ToList();
        void Cards(List<PKSimpleCardInfo> result,LocalPlayer q){var deck=q.Raids.BestDecks.GetValueOrDefault(id)??[];result.AddRange(Enumerable.Range(0,20).Select(i=>i<deck.Count?deck[i].Pack():new PKSimpleCardInfo()));}
        int index=all.FindIndex(x=>x.Uid==p.Uid);var mine=new PKWorldBossUserRankData{ranking=index<0?0u:(uint)index+1,damage=p.Raids.BestDamage.GetValueOrDefault(id)};Cards(mine.cardInfo,p);
        var r=new GsGetWorldBossTopRankerOk{bossId=id,userRankData=mine};uint rank=0;
        foreach(var q in all.Take(100)){var row=new PKWorldBossTopRankerData{ranking=++rank,damage=q.Raids.BestDamage[id],rankUserInfo=RankingUser(q)};Cards(row.cardInfo,q);r.topRankerList.Add(row);}
        r.currentCount=r.totalCount=(uint)r.topRankerList.Count;return r;
    }
    private Reply? HandleRanking(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        InitRanking(p);foreach(var seededGuild in AllGuilds)GuildDonations(seededGuild);
        if(request is GsGetChangedMissionListRequest changed&&changed.needBattleAbilityRefresh){p.Ranking.HasPower=true;p.Ranking.TigerPower=changed.whiteTigerBattleAbility;p.Ranking.DragonPower=changed.blueDragonBattleAbility;p.Ranking.SnakePower=changed.redSnakeBattleAbility;p.Ranking.PowerUpdatedAt=Now;store.Save();return null;}
        if(request is GsCheckGameEntryRequest poll){
            if(poll.whiteTigerBattleAbility!=0||poll.blueDragonBattleAbility!=0||poll.redSnakeBattleAbility!=0){p.Ranking.HasPower=true;p.Ranking.TigerPower=poll.whiteTigerBattleAbility;p.Ranking.DragonPower=poll.blueDragonBattleAbility;p.Ranking.SnakePower=poll.redSnakeBattleAbility;p.Ranking.PowerUpdatedAt=Now;}
            p.Ranking.ArenaPower=poll.ArenaDefenceDeckBattleAbility;p.Ranking.InfinitePower=poll.InfiniteArenaDefenceDeckBattleAbility;store.Save();return null;
        }
        if(request is GsGetWorldBossTopRankerRequest boss){if(!Raids.World.Any(x=>x.Id==boss.bossId))return fail("unknown world boss",ServerErrorCode.ERROR_INVALID_ID);return ok(RankingWorldBoss(p,boss.bossId));}
        if(request is GsGetRankingBoardTopRankerRequest top){if(!Enum.IsDefined(top.rankingBoardType))return fail("unknown ranking board",ServerErrorCode.ERROR_INVALID_ID);var r=RankingBoard(p,top.rankingBoardType);store.Save();return ok(r);}
        if(name=="GS_GET_LIKE_YOU_INFO_REQUEST"){store.Save();return ok(RankingLikes(p));}
        if(request is GsSendLikeYouRequest like){
            return FeatureReceipt(p,p.Ranking.Receipts,name,session,payload,()=>{
                var q=store.Find(like.targetUid);if(q==null||q.Uid==p.Uid)return fail("likes require another local player",ServerErrorCode.ERROR_INVALID_ID);InitRanking(q);var info=RankingLikes(p);
                if(info.remainLikeYouPoint==0||p.Ranking.Liked.Contains(q.Uid)||q.Ranking.ReceivedToday>=catalog.Numbers[BscTable.NUMBER_TYPE.DAILY_LIKE_GET_COUNT])return fail("daily like quota or recipient limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                p.Ranking.Liked.Add(q.Uid);q.Ranking.ReceivedToday++;q.Ranking.ReceivedTotal++;info=RankingLikes(p);var r=new GsSendLikeYouOk{remainLikeYouPoint=info.remainLikeYouPoint};r.likeyouSendTargetUidList.AddRange(info.likeyouSendTargetUidList);return ok(r);
            },fail);
        }
        if(request is GsGetRankingBoardCommonDetailInfoRequest common){var q=store.Find(common.targetUid);return q==null?fail("ranked player not found",ServerErrorCode.ERROR_INVALID_ID):ok(new GsGetRankingBoardCommonDetailInfoOk{targetUid=q.Uid,userData=GuildThumbnail(q),guildInfo=RankingGuild(q)});}
        if(request is GsGetRankingBoardArenaBattleDetailInfoRequest arena){
            var q=store.Find(arena.targetUid)??Bots.Find(arena.targetUid)?.Profile;if(q==null||arena.rankingBoardType is not (RankType.RANKING_BOARD_ARENA_BATTLE_REAL_TIME or RankType.RANKING_BOARD_INFINITE_ARENA_BATTLE_REAL_TIME))return fail("this PvP record is unavailable",ServerErrorCode.ERROR_INVALID_ID);
            bool infinite=arena.rankingBoardType==RankType.RANKING_BOARD_INFINITE_ARENA_BATTLE_REAL_TIME;var r=new GsGetRankingBoardArenaBattleDetailInfoOk{targetUid=q.Uid,userData=GuildThumbnail(q),guildInfo=RankingGuild(q),userRankData=RankingFor(q,arena.rankingBoardType),battleAbility=infinite?q.Ranking.InfinitePower:q.Ranking.ArenaPower};var deck=q.Decks.GetValueOrDefault((uint)(infinite?BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_INFINITE_ARENA:BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_ARENA))??q.Deck;
            for(uint i=0;i<deck.Count;i++)if(deck[(int)i]>0&&Owns(q,deck[(int)i]))r.battleCardInfoList.Add(RankingCard(q,deck[(int)i],i));return ok(r);
        }
        if(request is GsGetRankingBoardWorldBossInfoRequest world){var q=store.Find(world.targetUid);if(q==null||world.rankingBoardType is not (RankType.RANKING_BOARD_WORLD_BOSS or RankType.RANKING_BOARD_WORLD_BOSS_COMBO))return fail("boss rank unavailable",ServerErrorCode.ERROR_INVALID_ID);return ok(new GsGetRankingBoardWorldBossDetailInfoOk{targetUid=q.Uid,userData=GuildThumbnail(q),guildInfo=RankingGuild(q),userRankData=RankingFor(q,world.rankingBoardType)});}
        if(request is GsGetRankingBoardWanderBattleDetailInfoRequest wander){var q=store.Find(wander.targetUid);return q==null?fail("ranked player not found",ServerErrorCode.ERROR_INVALID_ID):ok(new GsGetRankingBoardWanderBattleDetailInfoOk{targetUid=q.Uid,userData=GuildThumbnail(q),guildInfo=RankingGuild(q),userRankData=RankingFor(q,RankType.RANKING_BOARD_WANDER_BATTLE_POINT)});}
        if(request is GsGetRankingBoardChainBattleDetailInfoRequest chain)
        {
            var target=store.Find(chain.targetUid);
            return target==null?fail("ranked player not found",ServerErrorCode.ERROR_INVALID_ID):ok(new GsGetRankingBoardChainBattleDetailInfoOk
            {
                targetUid=target.Uid,userData=GuildThumbnail(target),guildInfo=RankingGuild(target),
                userRankData=RankingFor(target,RankType.RANKING_BOARD_CHAIN_BATTLE)
            });
        }
        if(request is GsGetRankingBoardGuildDetailInfoRequest guild){var g=Guild(guild.targetGuildId);if(g==null)return fail("guild not found",ServerErrorCode.ERROR_INVALID_GUILD_INFO);var rows=RankedGuilds();int i=rows.FindIndex(x=>x.Guild.Id==g.Id);return ok(new GsGetRankingBoardGuildDetailInfoOk{targetGuildId=g.Id,guildInfo=GuildInfo(g),guildContributionRank=i<0?0u:(uint)i+1});}
        if(name is "GS_GET_RAINKING_BOARD_RANK_BATTLE_DETAIL_REQUEST" or "GS_GET_RAINKING_BOARD_BALANCE_BATTLE_DETAIL_REQUEST" or "GS_GET_RAINKING_BOARD_CHAIN_BATTLE_DETAIL_REQUEST")return fail("competitive match records have not been restored locally",ServerErrorCode.ERROR_INVALID_ID);
        if(request is GsGetChargeRankingListRequest charge)return ok(new GsGetChargeRankingListOk{eventId=charge.eventId,index=charge.index});
        return null;
    }
}
