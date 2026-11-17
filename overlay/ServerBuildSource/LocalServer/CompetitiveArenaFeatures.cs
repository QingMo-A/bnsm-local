internal sealed partial class ProtocolRouter
{
    private Reply ExecuteCompetitiveArena(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);
        uint mode=request is LocalRankCombatRequest local?local.mode:name.Contains("INFINIT",StringComparison.Ordinal)?31u:12u;
        var pending=p.Competitive;
        if(request is LocalRankCombatRequest {operation:3} cancel&&(cancel.stamp==0||cancel.stamp!=pending.Stamp||pending.ActiveMode!=mode))return fail("stale competitive cancel cannot clear a newer battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        var record=mode==18?new LocalPvpRecord():PvpRecord(p,mode);
        if(request is LocalRankCombatRequest read&&read.operation==0) {
            if(pending.ManualControl&&pending.ActiveMode==10){ClearCompetitive(p);store.Save();}
            RefreshCompetitiveLadderOffers(p,10);
            var result=new GsGetRankBattleRecordOk{rankBattleRecord=RankRecord(p,10),nextBattleRecordResetTime=ShopNextReset};
            result.rankBattleHistory.AddRange(PvpRecord(p,10).History.Select(x=>(PKRankBattleHistoryData)WireCodec.Deserialize(typeof(PKRankBattleHistoryData),x)!));return ok(result);
        }
        if(name=="GS_GET_BALANCE_BATTLE_RECORD_REQUEST"){
            if(pending.ManualControl&&pending.ActiveMode==42){ClearCompetitive(p);store.Save();}
            RefreshCompetitiveLadderOffers(p,42);
            return ok(new GsGetBalanceBattleRecordOk{balanceBattleRecord=(PKBalanceBattleRecord)WireCodec.Deserialize(typeof(PKBalanceBattleRecord),WireCodec.Serialize(RankRecord(p,42)))!,nextBattleRecordResetTime=ShopNextReset});
        }
        if(request is LocalRankCombatRequest rank) {
            if(mode==18&&rank.operation==13)return ok(CastleSessionInfo(p,pending.ActiveMode==18?pending.Stamp:0,13));
            if(mode==18&&rank.operation==14){
                if(rank.stamp==0||rank.stamp!=pending.Stamp||pending.ActiveMode!=18||pending.Started||p.ActiveStage!=null||rank.castleResult==null)
                    return fail("instant castle action requires its current unstarted encounter; reopen the selected castle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
                return HandleCompetitiveCastle("GS_UPDATE_CASTLE_BATTLE_RESULT_REQUEST",rank.castleResult,p,message=>ok(new LocalRankCombatReply{stamp=rank.stamp,operation=14,castleResult=(GsUpdateCastleBattleResultOk)message!}),fail,true,true)!;
            }
            if(mode==18&&rank.operation==11){
                if(rank.stamp==0||rank.stamp!=pending.Stamp||pending.ActiveMode!=18||!pending.Started||rank.castleResult==null)return fail("stale castle settlement does not match the active battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
                return HandleCompetitiveCastle("GS_UPDATE_CASTLE_BATTLE_RESULT_REQUEST",rank.castleResult,p,message=>ok(new LocalRankCombatReply{stamp=rank.stamp,operation=11,castleResult=(GsUpdateCastleBattleResultOk)message!}),fail,true)!;
            }
            if(mode==18&&rank.operation==12){
                if(rank.stamp==0||rank.stamp!=pending.Stamp||pending.ActiveMode!=18||!pending.Started||p.ActiveBattleStamp!=rank.stamp)return fail("stale castle retreat does not match the active battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
                ClearCompetitive(p);return ok(CastleSessionInfo(p,rank.stamp,12));
            }
            if(mode==18&&rank.operation==4) {
                if(pending.ActiveMode!=18||pending.Started||p.ActiveStage!=null)return fail($"castle encounter unavailable: mode={pending.ActiveMode} started={pending.Started} activeStage={p.ActiveStage} stamp={pending.Stamp} submitted={rank.stamp} target={pending.CastleTarget} map={pending.Map} spot={pending.Spot}",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
                if(rank.stamp!=0&&rank.stamp!=pending.Stamp)return fail("castle encounter changed; reopen the selected castle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
                if(CastleRosterError(p,rank.cards,pending.Action==1,out var cards) is {} rosterError)return fail($"castle start roster invalid: action={pending.Action} cards={string.Join(',',rank.cards)} fallen={string.Join(',',pending.CastleDeadCards)}",rosterError);
                var tile=Competitive.CastleMaps.Single(x=>x.MapId==pending.Map).SpotList.Single(x=>x.SpotId==pending.Spot);var castle=Competitive.Castles.Single(x=>x.Id==tile.CastleId);
                if(!CastleRosterAllowed(p,castle,cards))return fail($"castle native roster restrictions: castle={castle.Id} faction={castle.FactionCond} attack={castle.AttackTypeCond} cards={string.Join(',',cards)}",ServerErrorCode.ERROR_INVALID_CARD_FACTION);
                var costs=CastleAdmissionCost(p,castle,pending.Action);int gold=costs[0],cash=costs[1],stamina=costs[2];
                if(costs.Any(x=>x<0))return Bad("invalid castle admission cost");
                if(p.Gold<gold||p.CashPoint<cash||p.Stamina<stamina)return fail("not enough castle admission resources",ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
                if(pending.Action==1&&pending.CastleAttacks>=catalog.VipFor(p.VipExp).CastleAttackCount&&pending.CastlePaidLeft==0)return fail("no castle attacks remain",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                pending.CastleStamped=rank.stamp!=0;
                if(pending.CastleStamped){p.Gold-=(uint)gold;p.CashPoint-=(uint)cash;p.Stamina-=(uint)stamina;pending.CastleAdmissionPaid=true;if(pending.Action==1){pending.CastleAttacks++;pending.CastleTargetAttacks++;if(pending.CastleAttacks>catalog.VipFor(p.VipExp).CastleAttackCount)pending.CastlePaidLeft--;}}
                p.Decks[54]=rank.cards.ToList();pending.Cards=cards;pending.Started=true;LocalBattleAttempts.Bind(p,0xF0000000u+18,pending.Stamp);ConsumeBsBalls(p,cards);
                return ok(CastleSessionInfo(p,pending.Stamp,4));
            }
            if(mode is not (10 or 12 or 31 or 42))return Bad("unsupported local competitive mode");
            if(!FeatureOpen(p,mode is 10 or 42?BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_REALTIME_PVP:BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_PVP))return fail("original competitive unlock level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(rank.operation==1) {
                if(p.ActiveStage!=null)return Bad("finish the current battle first");
                if(record.Used>=PvpLimit(p,mode)&&record.PaidLeft==0)return fail("no ranked entries remaining",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                uint deckId=mode==12?50u:mode==31?57u:52u;
                if(!CompetitiveRoster(p,rank.cards.Count>0?rank.cards:DeckSlots(p,deckId),mode==31?15:5,out var cards)||cards.Count<(mode==31?3:mode==12?1:5))return Bad("competitive battle needs its distinct owned available attack cards");
                bool manual=rank.manualControlVersion==1&&mode is 10 or 42;
                var reserves=rank.reserveCards.ToList();
                if(rank.manualControlVersion>1||(!manual&&(rank.manualControlVersion!=0||reserves.Count!=0))||
                    (manual&&(reserves.Count>RankReserveLimit(mode)||reserves.Any(x=>x==0||!Owns(p,x)||InTraining(p,x))||reserves.Distinct().Count()!=reserves.Count||reserves.Intersect(cards).Any())))
                    return fail("invalid competitive reserve roster",ServerErrorCode.ERROR_INVALID_SIDE_DECK_CARD_INFO);
                var opponents=CompetitiveOpponents(p,mode);if(opponents.Count==0)return Bad("no bot opponents configured");var enemy=rank.targetUid==0?AutomaticCompetitiveOpponent(p,mode,record.Battles):opponents.FirstOrDefault(x=>x.Uid==rank.targetUid);
                if(enemy==null)return Bad("competitive opponent is unavailable");
                if(mode==31&&((rank.cards.Count>0?rank.cards:DeckSlots(p,deckId)).Count!=15||Enumerable.Range(0,3).Any(team=>!(rank.cards.Count>0?rank.cards:DeckSlots(p,deckId)).Skip(team*5).Take(5).Any(x=>x!=0))))return fail("infinite deck needs one to five cards in each team",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
                var selectedOffer=CompetitiveOffers(p,mode).Single(x=>x.Uid==enemy.Uid);
                if(mode==31)enemy=InfiniteEnemyRoster(p,enemy);
                var enemyReserves=manual?RankEnemyReserves(p,mode,enemy):new List<(LocalPlayer Player,ulong Sequence)>();
                p.Decks[deckId]=(rank.cards.Count>0?rank.cards:DeckSlots(p,deckId)).ToList();BeginCompetitive(p,mode,enemy,cards);if(record.Used<PvpLimit(p,mode))record.Used++;else record.PaidLeft--;record.Latest=Now;pending.Started=true;LocalBattleAttempts.Bind(p,0xF0000000u+mode,pending.Stamp);ConsumeBsBalls(p,cards);
                pending.OpponentRank=selectedOffer.Rank;
                var match=new LocalRankCombatReply{operation=1,record=RankRecord(p,mode),stamp=pending.Stamp,enemy=ArenaEnemy(enemy,selectedOffer.Badge,selectedOffer.Rank),coins=Coins(p)};
                match.enemyCards.AddRange(enemy.Deck.Select((seq,i)=>mode==42?BalancedCard(enemy,seq,(uint)i):RankingCard(enemy,seq,(uint)i)));
                if(manual){
                    pending.ManualControl=true;pending.ReserveCards=reserves;match.reserveCards.AddRange(reserves);match.manualControlVersion=1;
                    var snapshot=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),pending.Enemy)!;
                    foreach(var (profile,seq) in enemyReserves){
                        uint slot=(uint)match.enemyCards.Count;var packet=mode==42?BalancedCard(profile,seq,slot):RankingCard(profile,seq,slot);packet.cardSeq=slot+1;match.enemyCards.Add(packet);
                        snapshot.cardInfoList.Add(mode==42?BalancedClone(profile,seq):CompetitiveClone(profile,seq));
                    }
                    pending.Enemy=WireCodec.Serialize(snapshot);ConsumeBsBalls(p,reserves);
                }
                return ok(match);
            }
            if(rank.operation==2) {
                if(pending.ActiveMode!=mode||pending.Stamp!=rank.stamp||rank.result>1||!rank.cards.Where(x=>x!=0).SequenceEqual(pending.Cards)||pending.ReplayWinner!=rank.result)return Bad("ranked result has no matching completed replay");
                var result=SettleCompetitivePvp(p,mode,rank.result==0);if(result==null)return Bad("competitive reward capacity exceeded");
                return ok(new LocalRankCombatReply{operation=2,stamp=rank.stamp,record=RankRecord(p,mode),settled=result,coins=Coins(p),nextReset=ShopNextReset});
            }
            if(rank.operation==3){if(rank.stamp==0||rank.stamp!=pending.Stamp||pending.ActiveMode!=mode)return fail("stale competitive cancel cannot clear a newer battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);ClearCompetitive(p);return ok(new LocalRankCombatReply{operation=3,stamp=rank.stamp,record=RankRecord(p,mode),coins=Coins(p)});}
            return Bad("unknown local ranked operation");
        }
        if(request is GsEnterArenaBattleRequest or GsEnterInfiniteArenaBattleRequest) {
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_PVP))return fail("original PvP unlock level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            ClearCompetitive(p);
            if(mode==31){var info=new GsEnterInfiniteArenaBattleOk{rank=record.Rank,bestRank=record.BestRank,todayRewardPoint=record.RewardPoints};info.todayRewardList.AddRange(record.RewardClaims);return ok(info);}
            return ok(new GsEnterArenaBattleOk{rank=record.Rank,bestRank=record.BestRank});
        }
        if(request is GsSearchArenaBattleEnemyListRequest or GsSearchInfiniteArenaBattleEnemyListRequest) {
            RefreshCompetitiveLadderOffers(p,mode);
            var list=CompetitiveOffers(p,mode).Select(offer=>ArenaEnemy(CompetitiveProfile(offer),offer.Badge,offer.Rank)).ToArray();
            if(mode==31){var result=new GsSearchInfiniteArenaBattleEnemyListOk{currentRank=record.Rank};result.enemyList.AddRange(list);return ok(result);}
            var arena=new GsSearchArenaBattleEnemyListOk{currentRank=record.Rank};arena.enemyList.AddRange(list);return ok(arena);
        }
        if(request is GsGetArenaBattleEnemyDeckInfoRequest or GsGetInfiniteArenaBattleEnemyDeckInfoRequest) {
            uint uid=(uint)Field(request,"targetUid")!;var enemy=CompetitiveOpponents(p,mode).FirstOrDefault(x=>x.Uid==uid);if(enemy==null)return Bad("enemy is not in the local competitive roster");
            if(p.ActiveStage!=null)return Bad("finish the current battle first");
            if(record.Used>=PvpLimit(p,mode)&&record.PaidLeft==0)return fail("no PvP entries remaining",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            // This packet is also used to inspect an opponent before choosing a
            // deck. Actual entry is explicit in the local start extension.
            if(mode==31) {
                var result=new GsGetInfiniteArenaBattleEnemyDeckInfoOk();
                var team=InfiniteEnemyRoster(p,enemy);
                for(uint i=0;i<team.CardIds.Count;i++)result.battleCardInfoList.Add(RankingCard(team,i+1,i));
                result.formationInfoList.AddRange(Enumerable.Range(0,3).Select(_=>new PKFormationInfo{id=1,level=1}));return ok(result);
            }
            var info=new GsGetArenaBattleEnemyDeckInfoOk{formationInfo=new(){id=1,level=1}};info.battleCardInfoList.AddRange(enemy.Deck.Select((seq,i)=>RankingCard(enemy,seq,(uint)i)));return ok(info);
        }
        if(request is GsUpdateArenaBattleResultRequest arenaEnd) {
            if(pending.ActiveMode!=12||pending.Opponent!=arenaEnd.targetUid||arenaEnd.battleResult>1||pending.ReplayWinner!=arenaEnd.battleResult||!arenaEnd.useCardSeq.Where(x=>x!=0).SequenceEqual(pending.Cards))return Bad("arena result has no matching completed replay");
            var result=SettleCompetitivePvp(p,12,arenaEnd.battleResult==0);return result==null?Bad("competitive reward capacity exceeded"):ok(result);
        }
        if(request is GsUpdateInfiniteArenaBattleResultRequest infiniteEnd) {
            if(pending.ActiveMode!=31||pending.Opponent!=infiniteEnd.targetUid||pending.ReplayResults.Count<2||infiniteEnd.battleResult>1||!infiniteEnd.useCardSeq.Where(x=>x!=0).SequenceEqual(pending.Cards))return Bad("infinite arena result has no matching rounds");
            bool won=pending.ReplayResults.Count(x=>x==0)>pending.ReplayResults.Count(x=>x==1);if(won!=(infiniteEnd.battleResult==0))return Bad("infinite arena rounds disagree with result");
            uint points=(uint)pending.ReplayResults.Count(x=>x==0);
            var result=SettleCompetitivePvp(p,31,won);if(result==null)return Bad("competitive reward capacity exceeded");
            if(record.RewardDay==0)record.RewardDay=TowerDay(Now);
            uint scoreCap=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.PVP_INFINITEARENA_MAX_SCORE];
            record.RewardPoints=(uint)Math.Min(scoreCap,(ulong)record.RewardPoints+points);
            return ok(new GsUpdateInfiniteArenaBattleResultOk{currentRank=result.currentRank,LastRewardRank=result.LastRewardRank,RewardInfo=result.RewardInfo,battleEndSyncData=result.battleEndSyncData,todayRewardPoint=record.RewardPoints});
        }
        if(request is GsGetArenaBattleHistoryRequest){var result=new GsGetArenaBattleHistoryOk();result.arenaBattleHistoryList.AddRange(record.History.Select(x=>(PKArenaBattleHistoryData)WireCodec.Deserialize(typeof(PKArenaBattleHistoryData),x)!));return ok(result);}
        if(request is GsGetInfiniteArenaBattleHistoryRequest){var result=new GsGetInfiniteArenaBattleHistoryOk();result.infiniteArenaBattleHistoryList.AddRange(record.History.Select(x=>(PKInfiniteArenaBattleHistoryData)WireCodec.Deserialize(typeof(PKInfiniteArenaBattleHistoryData),x)!));return ok(result);}
        if(name=="GS_GET_INFINIT_ARENA_DECK_INFO_REQUEST"||request is GsUpdateInfiniteArenaUserDeckInfoRequest) {
            if(request is GsUpdateInfiniteArenaUserDeckInfoRequest set) {
                if(set.infiniteArenaDeckInfo==null||set.infiniteArenaDeckInfo.userDeck.Count!=15||!CompetitiveRoster(p,set.infiniteArenaDeckInfo.userDeck,15,out var cards)||Enumerable.Range(0,3).Any(team=>!set.infiniteArenaDeckInfo.userDeck.Skip(team*5).Take(5).Any(x=>x!=0)))return fail("infinite defence needs one to five distinct available cards in each team",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
                p.Decks[56]=set.infiniteArenaDeckInfo.userDeck.ToList();
            }
            var info=new PKInfiniteArenaUserDeckInfo();info.userDeck.AddRange(DeckSlots(p,56).Concat(new ulong[15]).Take(15));info.formationId.AddRange([0u,0u,0u]);
            return request is GsUpdateInfiniteArenaUserDeckInfoRequest?ok(new GsUpdateInfiniteArenaUserDeckInfoOk{userDeckInfo=info}):ok(new GsGetInfiniteArenaUserDeckInfoOk{userDeckInfo=info});
        }
        return Bad("competitive operation not restored");
    }
    private LocalPlayer InfiniteEnemyRoster(LocalPlayer viewer,LocalPlayer seed)
    {
        var result=new LocalPlayer{Uid=seed.Uid,Account=seed.Account,Nickname=seed.Nickname,Level=seed.Level};
        var seedOffer=CompetitiveOffers(viewer,31).Single(x=>x.Uid==seed.Uid);
        var teams=CompetitiveOffers(viewer,31).Where(x=>x.Badge==seedOffer.Badge).OrderBy(x=>x.Uid==seed.Uid?0:1).Take(3).Select(x=>{
            var copy=System.Text.Json.JsonSerializer.Deserialize<LocalCompetitiveOffer>(System.Text.Json.JsonSerializer.Serialize(x))!;
            copy.Grade=seedOffer.Grade;copy.Level=seedOffer.Level;copy.LadderVersion=seedOffer.LadderVersion;copy.GrowthStage=seedOffer.GrowthStage;
            return CompetitiveProfile(copy);
        }).ToList();
        while(teams.Count<3){var copy=System.Text.Json.JsonSerializer.Deserialize<LocalCompetitiveOffer>(System.Text.Json.JsonSerializer.Serialize(seedOffer))!;copy.Seed=unchecked(copy.Seed+7919*teams.Count);teams.Add(CompetitiveProfile(copy));}
        foreach(var team in teams)foreach(ulong seq in team.Deck) {
            result.CardIds.Add(team.CardIds[(int)seq-1]);ulong next=(ulong)result.CardIds.Count;var state=System.Text.Json.JsonSerializer.Deserialize<LocalCardState>(System.Text.Json.JsonSerializer.Serialize(team.CardStates[seq]))!;
            var map=new Dictionary<ulong,ulong>();foreach(var item in team.EquipmentItems.Where(x=>x.Sequence==state.WeaponSequence||state.AccessorySequences.Contains(x.Sequence))){ulong id=result.NextEquipmentSequence++;map[item.Sequence]=id;var copy=System.Text.Json.JsonSerializer.Deserialize<LocalOwnedEquipment>(System.Text.Json.JsonSerializer.Serialize(item))!;copy.Sequence=id;result.EquipmentItems.Add(copy);}
            state.WeaponSequence=map.GetValueOrDefault(state.WeaponSequence);state.AccessorySequences=state.AccessorySequences.Select(x=>map.GetValueOrDefault(x)).ToList();result.CardStates[next]=state;result.Deck.Add(next);
        }
        return result;
    }
    private BscTable.BalanceCardPvpData BalanceRow(uint cardId)
    {
        int family=catalog.RequireCard(cardId).EvolveFirstAncestor;
        return Competitive.BalanceCards.Single(x=>catalog.RequireCard(x.CardId).EvolveFirstAncestor==family);
    }
    private PKBattleDeckCardInfo BalancedCard(LocalPlayer p,ulong seq,uint slot)
    {
        var row=BalanceRow(p.CardIds[(int)seq-1]);var result=new PKBattleDeckCardInfo{uid=p.Uid,cardSeq=seq,cardId=(uint)row.CardId,cardLevel=(uint)row.Level,enchantGrade=(uint)row.Rank,slotIndex=slot};
        result.cardSkillLevel.AddRange(Enumerable.Repeat((uint)row.SkillLevel,7));result.cardSkillEnchant.AddRange(new uint[7]);
        result.cardEquipInfo.AddRange(new[]{row.Weapone,row.Necklane,row.Earring,row.Gem1,row.Gem2,row.Gem3}.Select(x=>new PKBattleDeckCardEquipInfo{itemID=(uint)x}));return result;
    }
    private PKCloneBattleEnemyInfo BalancedEnemy(LocalPlayer p)
    {
        var result=CompetitiveEnemy(p);result.cardInfoList.Clear();foreach(ulong seq in p.Deck){var info=BalancedCard(p,seq,0);var clone=new PKCloneBattleCardInfo{cardId=info.cardId,cardLevel=info.cardLevel,cardEnchant=info.enchantGrade};clone.skillLevel.AddRange(info.cardSkillLevel);clone.cardSkillEnchant.AddRange(info.cardSkillEnchant);clone.cardEquipInfo.AddRange(info.cardEquipInfo.Select(x=>x.itemID));result.cardInfoList.Add(clone);}return result;
    }
    private PKCloneBattleCardInfo BalancedClone(LocalPlayer p,ulong seq)
    {
        var info=BalancedCard(p,seq,0);var clone=new PKCloneBattleCardInfo{cardId=info.cardId,cardLevel=info.cardLevel,cardEnchant=info.enchantGrade};
        clone.skillLevel.AddRange(info.cardSkillLevel);clone.cardSkillEnchant.AddRange(info.cardSkillEnchant);clone.cardEquipInfo.AddRange(info.cardEquipInfo.Select(x=>x.itemID));return clone;
    }
    private PKCloneBattleEnemyInfo InfiniteEnemySnapshot(LocalPlayer team){var result=CompetitiveEnemy(team);return result;}
    private GsUpdateArenaBattleResultOk? SettleCompetitivePvp(LocalPlayer p,uint mode,bool won)
    {
        var c=p.Competitive;var r=PvpRecord(p,mode);uint old=r.Rank;
        uint rank=won&&c.OpponentRank>0?Math.Min(old,c.OpponentRank):old;
        uint rewardRank=mode==31&&rank>=r.BestRank?0:rank;
        var reward=Competitive.Rewards.Where(x=>x.PvpBattleType==PvpType(mode)&&x.BattleResultType==(mode is 10 or 42?(won?BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_WIN:BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_LOSE):BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_RANKING_RISE))
            .Where(x=>mode is 10 or 42||won&&rewardRank!=0&&x.Ranking_Min<=rewardRank&&(x.Ranking_Max==0||rewardRank<=x.Ranking_Max)).OrderBy(x=>x.Ranking_Min).FirstOrDefault();
        var displayed=new PKRewardItemInfo{rewardItemType=BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT};
        if(reward!=null&&reward.ItemQuantity.Count>0) {
            if(!GrantCompetitiveRewards(p,reward.RewardValueIdType,reward.RewardValueId,reward.ItemQuantity,out _,out _))return null;
            displayed=new(){rewardItemType=reward.RewardValueIdType[0],rewardItemData=(ulong)reward.RewardValueId[0],rewardItemCount=(uint)reward.ItemQuantity[0]};
        }
        r.LossStreak=won?0:r.LossStreak+1;r.Battles++;r.Rank=rank;r.BestRank=Math.Min(r.BestRank,rank);if(won){r.Wins++;r.Streak++;r.MaxStreak=Math.Max(r.MaxStreak,r.Streak);r.Points+=50;}else{r.Streak=0;r.Points=r.Points>10?r.Points-10:0;}
        if(mode==12)p.Mailbox.ArenaRanks.Add(new(){Time=Now,Rank=rank});
        if(mode==31)p.Mailbox.InfiniteArenaRanks.Add(new(){Time=Now,Rank=rank});
        var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),c.Enemy)!;var history=new PKArenaBattleHistoryData{battleTime=Now,battleResult=won?0u:1u,changedRank=rank,enemyUid=c.Opponent,replaySeq=c.Stamp,enemyCommonInfo=enemy.commonInfo,enemyCardId=enemy.cardInfoList.FirstOrDefault()?.cardId??0,enemyCardLevel=enemy.cardInfoList.FirstOrDefault()?.cardLevel??1};
        byte[] bytes=mode is 10 or 42?WireCodec.Serialize(new PKRankBattleHistoryData{battleTime=Now,battleResult=won?0u:1u,enemyUid=history.enemyUid,enemyCardId=history.enemyCardId,enemyCommonInfo=history.enemyCommonInfo,replaySeq=history.replaySeq}):WireCodec.Serialize(history);
        if(mode==31)bytes=WireCodec.Serialize(new PKInfiniteArenaBattleHistoryData{battleTime=Now,battleResult=history.battleResult,changedRank=rank,enemyUid=history.enemyUid,enemyCommonInfo=history.enemyCommonInfo,enemyCardId=history.enemyCardId,enemyCardLevel=history.enemyCardLevel,battleRoundResult1=c.ReplayResults.ElementAtOrDefault(0),battleRoundResult2=c.ReplayResults.ElementAtOrDefault(1),battleRoundResult3=c.ReplayResults.Count>2?c.ReplayResults[2]:2,replaySeq1=c.Stamp,replaySeq2=c.Stamp+1,replaySeq3=c.ReplayResults.Count>2?c.Stamp+2:0});
        r.History.Insert(0,bytes);if(r.History.Count>50)r.History.RemoveAt(50);
        RecordCompetitiveObjective(p,mode,won);p.Competitive.Offers.Remove(mode);ClearCompetitive(p);
        return new(){currentRank=rank,LastRewardRank=old,RewardInfo=displayed,battleEndSyncData=BallSync(p)};
    }
    private void RecordCompetitiveObjective(LocalPlayer p,uint mode,bool won)
    {
        string suffix=mode==29?"GUILD_BATTLE":mode==10?"RANKING_PVP":mode==42?"BALANCE_PVP":mode==31?"INFINITE_ARENA_PVP":"ARENA_PVP";
        if (Enum.TryParse<BscTable.CHECK_TYPE>("MCT_ENTER_" + suffix, out var entered)) RecordObjective(p, entered);
        if (won && Enum.TryParse<BscTable.CHECK_TYPE>("MCT_WIN_" + suffix, out var victory)) RecordObjective(p, victory);
    }
    private bool GrantCompetitiveRewards(LocalPlayer p,IReadOnlyList<BscTable.REWARD_TYPE> types,IReadOnlyList<int> ids,IReadOnlyList<int> quantities,out List<PKUserItem> items,out List<PKUserCardInfo> cards)
    {
        items=[];cards=[];if(types.Count!=ids.Count||types.Count!=quantities.Count)return false;var grant=new ShopGrant();ulong arena=0,rank=0,exp=0;
        for(int i=0;i<types.Count;i++) {
            if(quantities[i]<0)return false;if(quantities[i]==0)continue;
            if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT)arena+=(uint)quantities[i];
            else if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_RANK_POINT)rank+=(uint)quantities[i];
            else if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_PC_EXP)exp+=(uint)quantities[i];
            else if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_GROUP_ITEM){if(!AddStageRow(p,grant,BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM,ids[i],quantities[i]))return false;}
            else if(!AddShopReward(p,grant,types[i],ids[i],quantities[i]))return false;
        }
        var growth=StageGrowth(p,exp,0);if(growth==null||(ulong)growth.Value.Stamina+grant.Stamina>uint.MaxValue)return false;
        if((ulong)p.ArenaPoint+arena>uint.MaxValue||(ulong)p.RankingPoint+rank>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return false;
        p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,items,cards);p.ArenaPoint+=(uint)arena;p.RankingPoint+=(uint)rank;return true;
    }
}
