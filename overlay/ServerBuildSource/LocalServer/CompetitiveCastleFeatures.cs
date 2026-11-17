internal sealed partial class ProtocolRouter
{
    private bool CastleBotOwns(LocalPlayer owner,BscTable.CastleSpotInfo tile)
    {
        var castle=Competitive.Castles.Single(x=>x.Id==tile.CastleId);
        return castle.BossId==0&&owner.Level>=tile.RequirePcLevel&&catalog.VipFor(owner.VipExp).VipLv>=tile.VIPLevel
            &&owner.Deck.Any(seq=>CastleRosterAllowed(owner,castle,[seq]));
    }
    private LocalPlayer CastleGuard(LocalPlayer owner,BscTable.CastleSpotInfo tile)
    {
        // Never change the cached offer or a resident's saved deck while adapting a defence.
        var copy=System.Text.Json.JsonSerializer.Deserialize<LocalPlayer>(System.Text.Json.JsonSerializer.Serialize(owner))!;
        var castle=Competitive.Castles.Single(x=>x.Id==tile.CastleId);
        copy.Deck=owner.Deck.Where(seq=>CastleRosterAllowed(owner,castle,[seq])).ToList();return copy;
    }
    private ServerErrorCode? CastleRosterError(LocalPlayer p,IEnumerable<ulong> slots,bool invasion,out List<ulong> cards)
    {
        cards=slots.Where(x=>x!=0).ToList();
        if(cards.Count is 0 or >5||cards.Distinct().Count()!=cards.Count)return ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO;
        if(cards.Any(x=>!Owns(p,x)))return ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE;
        if(cards.Any(x=>InTraining(p,x)))return ServerErrorCode.ERROR_CARD_IN_TRAINING;
        return invasion&&cards.Any(p.Competitive.CastleDeadCards.Contains)?ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_CARD:null;
    }
    private bool CastleRosterAllowed(LocalPlayer p,BscTable.CastleData castle,IEnumerable<ulong> cards)=>cards.All(seq=>{
        var card=catalog.RequireCard(p.CardIds[(int)seq-1]);
        return (castle.FactionCond==BscTable.CARD_FACTION.FACTION_ALL||card.Faction==castle.FactionCond)
            &&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_FORCE||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE)
            &&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_PHYSICAL||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_PHYSICAL);
    });
    private int[] CastleAdmissionCost(LocalPlayer p,BscTable.CastleData castle,uint action)
    {
        if(action==1){var price=catalog.Expenses.FindTryTimeData(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_CASTLE_BATTLE_ATTACK_SAME_USER,(int)Math.Min(10,p.Competitive.CastleTargetAttacks+1));return [castle.PlunderConsumeGold,castle.PlunderConsumeGem,price?.StaminaValue??castle.FirstConsumeStamina];}
        return action==0?[castle.FirstConsumeGold,castle.FirstConsumeGem,castle.FirstConsumeStamina]:[castle.RestoreConsumeGold,castle.RestoreConsumeGem,castle.RestoreConsumeStamina];
    }
    private LocalRankCombatReply CastleSessionInfo(LocalPlayer p,ulong stamp=0,uint operation=0)=>new(){stamp=stamp,operation=operation,coins=Coins(p),stamina=p.Stamina,staminaStamp=p.StaminaStamp,castlePlay=CastlePlay(p),castleTargetAttacks=p.Competitive.CastleTargetAttacks,activeBattleStamp=p.ActiveBattleStamp};
    private BscTable.CastleMapData CastleHome(LocalPlayer p)
    {
        var c=p.Competitive;if(c.HomeMap==0){var maps=Competitive.CastleMaps.Where(x=>x.Prob>0).ToArray();c.HomeMap=(uint)maps[PubWeighted(maps.Select(x=>x.Prob).ToArray())].MapId;}return Competitive.CastleMaps.Single(x=>x.MapId==c.HomeMap);
    }
    private LocalCastleSpot? CastleSpot(LocalPlayer p,uint map,uint spot)=>p.Competitive.Castles.GetValueOrDefault(map+"/"+spot);
    private void AccumulateCastleWealth(LocalCastleSpot spot,BscTable.CastleData castle)
    {
        if(spot.Status==1&&spot.WealthStamp>0&&Now>=spot.WealthStamp) {
            ulong cycle=(ulong)Math.Max(1,catalog.Numbers[BscTable.NUMBER_TYPE.CASTLE_WEALTH_ACCUM_CYCLE]);ulong ticks=(Now-spot.WealthStamp)/cycle;
            if(ticks>0){spot.Wealth=(uint)Math.Min((ulong)(spot.Upgraded?castle.UpWealthPointMax:castle.WealthPointMax),spot.Wealth+ticks*(uint)Math.Max(0,spot.Upgraded?castle.UpWealthPointDelta:castle.WealthPointDelta));spot.WealthStamp+=ticks*cycle;}
        }
    }
    private List<PKCastleBattleUpgradeMapSpot> UpgradeLinkedCastles(LocalPlayer p,BscTable.CastleMapData map)
    {
        var rows=map.SpotList.Where(row=>Competitive.Castles.Any(x=>x.Id==row.CastleId&&x.BossId==0)).ToDictionary(row=>row.SpotId);
        var upgrades=new List<PKCastleBattleUpgradeMapSpot>();
        foreach(var row in rows.Values)
        {
            uint mapId=(uint)map.MapId;
            var spot=CastleSpot(p,mapId,(uint)row.SpotId);
            if(spot is not {Status:1,Upgraded:false})continue;
            var links=row.LinkSpot.Where(rows.ContainsKey).ToArray();
            if(links.Length==0||links.Any(id=>CastleSpot(p,mapId,(uint)id)?.Status!=1))continue;
            AccumulateCastleWealth(spot,Competitive.Castles.Single(x=>x.Id==row.CastleId));
            spot.Upgraded=true;
            upgrades.Add(new(){mapId=mapId,spotId=spot.Spot});
        }
        return upgrades;
    }
    private PKCastleBattleMapSpotInfo CastleSpotInfo(LocalPlayer p,LocalCastleSpot spot)
    {
        var row=Competitive.CastleMaps.Single(x=>x.MapId==spot.Map).SpotList.Single(x=>x.SpotId==spot.Spot);var castle=Competitive.Castles.Single(x=>x.Id==row.CastleId);
        AccumulateCastleWealth(spot,castle);
        var info=new PKCastleBattleMapSpotInfo{mapId=spot.Map,spotId=spot.Spot,spotStatus=(CastleBattleStatus)spot.Status,castleId=(uint)row.CastleId,upgradeFlag=spot.Upgraded,wealthPoint=spot.Wealth};
        if(castle.BossId>0)FillCastleRaidInfo(p,p,spot,info);
        if(spot.Status==1)
        {
            RotateCastleStore(p,spot,row);
            for(int i=0;i<row.GoodsId.Count;i++)info.storeInfoList.Add(new(){storeGoodsId=spot.StoreGoods[i],storeMoneytype=row.MoneyType[i],storeBuyCount=spot.Purchases.GetValueOrDefault((uint)i),slotFactor=(float)row.MoneyFactor[i]});
        }
        return info;
    }
    private PKCastleBattleMapSpotDetailInfo CastleDetail(LocalCastleSpot spot)
    {
        var cards=spot.Defence.Concat(new ulong[5]).ToArray();return new(){mapId=spot.Map,spotId=spot.Spot,defencecardSlot1=cards[0],defencecardSlot2=cards[1],defencecardSlot3=cards[2],defencecardSlot4=cards[3],defencecardSlot5=cards[4],attackedTime=spot.Attacked,attackerUid=spot.Attacker,totalAttackedCount=spot.Attacks,totalDefeatCount=spot.Defeats,StoreResetCount=spot.StoreReset,restoreHelperNick=""};
    }
    private PKCastleBattlePlayInfo CastlePlay(LocalPlayer p)
    {
        var c=p.Competitive;var info=new PKCastleBattlePlayInfo{todayOccupyBattleSkipCount=c.CastleSkips.GetValueOrDefault(0u),todayAttackBattleSkipCount=c.CastleSkips.GetValueOrDefault(1u),todayRestoreBattleSkipCount=c.CastleSkips.GetValueOrDefault(2u),todayAttackCount=c.CastleAttacks,totalAttackCount=c.CastleAttacks,todayExchangeCount=c.CastleExchanges,totalExchangeCount=c.CastleExchanges,todayExchangeWealthPoint=c.CastleExchangeWealth,totalExchangeWealthPoint=c.CastleExchangeWealth,attackMatchTryCount=c.CastleSearches,todayDeadCardRecoveryCount=c.CastleRecoveries,todayRestoreCastleHelpCount=c.CastleRestoreHelps,buyBonusAttackCountInfo=new(){todayBuyBonusCount=c.CastleBought,remainPaidCount=c.CastlePaidLeft},buyBonusExchangeCountInfo=new(){todayBuyBonusCount=c.CastleExchangeBought,remainPaidCount=c.CastleExchangePaidLeft}};info.haveMapIdList.Add((uint)CastleHome(p).MapId);return info;
    }
    private bool PayCompetitiveCost(LocalPlayer p,IReadOnlyList<BscTable.REWARD_TYPE> kinds,IReadOnlyList<int> ids,IReadOnlyList<int> amounts)
    {
        if(kinds.Count!=ids.Count||ids.Count!=amounts.Count)return false;ulong gold=0,cash=0,stamina=0,arena=0;var items=new Dictionary<uint,int>();
        for(int i=0;i<kinds.Count;i++) {
            if(amounts[i]<0)return false;uint n=(uint)amounts[i];switch(kinds[i]) {
                case BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT:arena+=n;break;case BscTable.REWARD_TYPE.REWARD_TYPE_GOLD:gold+=n;break;case BscTable.REWARD_TYPE.REWARD_TYPE_CASH:cash+=n;break;case BscTable.REWARD_TYPE.REWARD_TYPE_STAMINA:stamina+=n;break;
                case BscTable.REWARD_TYPE.REWARD_TYPE_ITEM:items[(uint)ids[i]]=checked(items.GetValueOrDefault((uint)ids[i])+(int)n);break;default:if(n!=0)return false;break;
            }
        }
        if(arena>p.ArenaPoint||gold>p.Gold||cash>p.CashPoint||stamina>p.Stamina||items.Any(x=>p.Inventory.GetValueOrDefault(x.Key)<x.Value))return false;
        p.ArenaPoint-=(uint)arena;p.Gold-=(uint)gold;p.CashPoint-=(uint)cash;p.Stamina-=(uint)stamina;foreach(var item in items)p.Inventory[item.Key]-=item.Value;return true;
    }
    private Reply? HandleCompetitiveCastle(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail,bool stampedResult=false,bool instantResult=false)
    {
        if(!name.Contains("CASTLE")&&request is not (GsSearchMapSpotRequest or GsExchangeWealthPointRequest or GsBatchExchangeWealthPointRequest))return null;
        Reply Bad(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);var c=p.Competitive;
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_CASTLE_BATTLE))return fail("original castle unlock level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var home=CastleHome(p);
        if(request is GsEnterCastleMapRequest) {
            if(p.Raids.ActiveMode is 6 or 7)AbandonRaidBattle(p);
            if(c.ActiveMode==18&&c.Started&&c.CastleStamped)return fail("retreat from the active castle battle before returning to its map",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(c.ActiveMode==18)ClearCompetitive(p);c.CastleTarget=0;c.CastleOpponent=null;c.CastleTargetLooted.Clear();c.CastleTargetAttacks=0;var result=new GsEnterCastleMapOk{castleBattlePlayInfo=CastlePlay(p)};
            UpgradeLinkedCastles(p,home);
            result.castleBattleMapSpotInfoList.AddRange(c.Castles.Values.Select(x=>CastleSpotInfo(p,x)));result.deadCardSeqList.AddRange(c.CastleDeadCards);result.myDefenceDeckCardSeqList.AddRange(c.Castles.Values.SelectMany(x=>x.Defence).Distinct());return ok(result);
        }
        if(request is GsGetCastleBattleMercenaryCardInfoRequest)return ok(new GsGetCastleBattleMercenaryCardInfoOk());
        if(request is GsGetCastleBattleHistoryRequest){var result=new GsGetCastleBattleHistoryOk();result.castleBattleHistoryList.AddRange(c.CastleHistory.Select(x=>(PKCastleBattleHistoryData)WireCodec.Deserialize(typeof(PKCastleBattleHistoryData),x)!));return ok(result);}
        if(request is GsGetCastleBattleAttackMatchRequest or GsGetFriendCastleMapInfoRequest or GsGetRevengeTargetCastleMapInfoRequest) {
            if(p.ActiveStage!=null)return fail("finish or retreat from the active battle before searching castles",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(request is GsGetFriendCastleMapInfoRequest visit&&store.Find(visit.targetUid) is {} owner&&CastleRaidAccessible(p,owner)){
                if(c.ActiveMode==18)ClearCompetitive(p);c.CastleTarget=owner.Uid;c.CastleOpponent=null;c.CastleTargetLooted.Clear();c.CastleTargetAttacks=0;
                var result=new GsGetFriendCastleMapInfoOk{targetUserInfo=GuildThumbnail(owner)};
                foreach(var saved in owner.Competitive.Castles.Values){var info=CastleSpotInfo(owner,saved);FillCastleRaidInfo(p,owner,saved,info);result.castleBattleMapSpotInfoList.Add(info);}return ok(result);
            }
            var enemies=CompetitiveOpponents(p,18);if(enemies.Count==0)return Bad("no local castle opponents");
            uint target=(uint?)Field(request,"targetUid")??0;var enemy=target==0?AutomaticCompetitiveOpponent(p,18,c.CastleSearches):enemies.FirstOrDefault(x=>x.Uid==target);if(enemy==null)return Bad("castle owner not found");
            if(request is GsGetCastleBattleAttackMatchRequest){
                if(c.Castles.Values.Count(x=>x.Status==1)<catalog.Numbers[BscTable.NUMBER_TYPE.CASTLE_ATTACK_CHECK_HAVE_CASTLE_COUNT])return fail("occupy the original required castle count before searching opponents",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
                if(c.CastleSearches>=int.MaxValue)return fail("castle search limit reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                var price=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RETRY_SEARCH_CASTLE_BATTLE_ENEMY,(int)c.CastleSearches+1,true);
                if(price is {Value:>0}){
                    if(price.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_GOLD)return Bad("unsupported original castle search currency");
                    if(p.Gold<price.Value)return fail("not enough gold for the displayed castle search price",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
                    p.Gold-=(uint)price.Value;
                }
                c.CastleSearches++;
            }
            if(c.ActiveMode==18)ClearCompetitive(p);c.CastleTarget=enemy.Uid;c.CastleOpponent=CompetitiveOffers(p,18).Single(x=>x.Uid==enemy.Uid);c.CastleTargetLooted.Clear();c.CastleTargetAttacks=0;
            var map=Competitive.CastleMaps.First();var spots=map.SpotList.Select(x=>{
                bool owns=CastleBotOwns(enemy,x);
                return new PKCastleBattleMapSpotInfo{mapId=(uint)map.MapId,spotId=(uint)x.SpotId,castleId=(uint)x.CastleId,spotStatus=owns?CastleBattleStatus.CASTLE_BATTLE_STATUS_OCCUPY:CastleBattleStatus.CASTLE_BATTLE_STATUS_NORMAL,wealthPoint=owns?(uint)Math.Max(0,Competitive.Castles.Single(row=>row.Id==x.CastleId).WealthPointMax):0};
            }).ToArray();
            if(request is GsGetFriendCastleMapInfoRequest){var result=new GsGetFriendCastleMapInfoOk{targetUserInfo=GuildThumbnail(enemy)};result.castleBattleMapSpotInfoList.AddRange(spots);return ok(result);}
            if(request is GsGetRevengeTargetCastleMapInfoRequest){var result=new GsGetRevengeTargetCastleMapInfoOk{targetUserInfo=GuildThumbnail(enemy)};result.castleBattleMapSpotInfoList.AddRange(spots);return ok(result);}
            var match=new GsGetCastleBattleAttackMatchOk{targetUserInfo=GuildThumbnail(enemy),coinInfo=Coins(p),attackMatchTryCount=c.CastleSearches};match.castleBattleMapSpotInfoList.AddRange(spots);return ok(match);
        }
        uint mapId=(uint?)Field(request,"mapId")??(uint)home.MapId,spotId=(uint?)Field(request,"spotId")??0;
        var mapRow=Competitive.CastleMaps.FirstOrDefault(x=>x.MapId==mapId);var row=mapRow?.SpotList.FirstOrDefault(x=>x.SpotId==spotId);var spot=CastleSpot(p,mapId,spotId);
        var support=CastleSupport(request,p,spot,row,ok,fail);if(support!=null)return support;
        if(request is GsSearchMapSpotRequest) {
            if(mapId!=home.MapId||row==null||spot!=null||p.Level<row.RequirePcLevel||catalog.VipFor(p.VipExp).VipLv<row.VIPLevel)return Bad("invalid castle exploration target");
            if(p.ActiveStage!=null)return fail("finish or retreat from the active battle before exploring castles",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(!LocalCastleMapRules.IsStartingSpot(row.RequirePcLevel)&&!LocalCastleMapRules.Neighbours((int)spotId).Any(link=>CastleSpot(p,mapId,(uint)link)!=null))return fail("explore a visible neighbouring hex first",ServerErrorCode.ERROR_INVALID_BATTLE_BOARD_SPOT_ID);
            if(!PayCompetitiveCost(p,row.ConsumeIdType,row.ConsumeId,row.ConsumeQuantity))return fail("not enough exploration cost",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            RecordObjective(p,BscTable.CHECK_TYPE.MCT_CASTLE_OPEN_SPOT);spot=new(){Map=mapId,Spot=spotId,WealthStamp=Now};c.Castles[mapId+"/"+spotId]=spot;
            return ok(new GsSearchMapSpotOk{coinInfo=Coins(p),stamina=p.Stamina,staminaStamp=p.StaminaStamp,castleMapSpotInfo=CastleSpotInfo(p,spot),castleMapSpotDetailInfo=CastleDetail(spot)});
        }
        if(request is GsGetCastleMapSpotDetailInfoRequest) {
            if(row==null)return fail("unknown castle spot",ServerErrorCode.ERROR_INVALID_CASTLE_DATA);
            if(request is GsGetCastleMapSpotDetailInfoRequest detail&&!detail.myCastleFlag){
                var enemy=CastleOpponent(p);if(enemy==null)return fail("castle visit missing; search a castle opponent first",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
                if(mapId!=Competitive.CastleMaps.First().MapId||!CastleBotOwns(enemy,row))return fail("castle target is not occupied by this owner",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
                spot=new(){Map=mapId,Spot=spotId,Status=1,Defence=CastleGuard(enemy,row).Deck};
            }
            return ok(new GsGetCastleMapSpotDetailInfoOk{mapId=mapId,spotId=spotId,castleBattleMapSpotDetailInfo=spot==null?new(){mapId=mapId,spotId=spotId}:CastleDetail(spot)});
        }
        if(request is GsGetTargetCastleDefenceDeckInfoRequest){
            if(row==null)return fail("unknown enemy castle spot",ServerErrorCode.ERROR_INVALID_CASTLE_DATA);var enemy=CastleOpponent(p);if(enemy==null)return fail("castle visit missing; search a castle opponent first",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
            if(mapId!=Competitive.CastleMaps.First().MapId||!CastleBotOwns(enemy,row))return fail("castle target is not occupied by this owner",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
            var guard=CastleGuard(enemy,row);var result=new GsGetTargetCastleDefenceDeckInfoOk();result.defenceDeckCardInfoList.AddRange(guard.Deck.Select((seq,i)=>RankingCard(guard,seq,(uint)i)));return ok(result);
        }
        if(request is GsCastleBattleOccupyMatchRequest or GsGetAttackedCastleInfoRequest) {
            if(row==null)return fail("unknown castle spot",ServerErrorCode.ERROR_INVALID_CASTLE_DATA);
            if(p.Level<row.RequirePcLevel)return fail($"castle level locked: map={mapId} spot={spotId} required={row.RequirePcLevel} actual={p.Level}",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(catalog.VipFor(p.VipExp).VipLv<row.VIPLevel)return fail($"castle VIP locked: required={row.VIPLevel}",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
            if(p.ActiveStage!=null)return fail($"castle prepare blocked by active battle: stage={p.ActiveStage} mode={c.ActiveMode} started={c.Started} stamp={p.ActiveBattleStamp}",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            bool intrusion=request is GsGetAttackedCastleInfoRequest attacked&&!attacked.myCastleFlag;
            if(!intrusion&&(mapId!=home.MapId||spot==null||spot.Status==1))return Bad("explore this castle first");
            LocalPlayer enemy;
            if(intrusion){
                enemy=CastleOpponent(p)!;if(enemy==null)return fail("castle visit missing; search a castle opponent first",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
                if(mapId!=Competitive.CastleMaps.First().MapId||!CastleBotOwns(enemy,row))return fail("castle target is not occupied by this owner",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
                if(c.CastleTargetLooted.Contains(mapId+"/"+spotId))return fail("castle already plundered during this visit",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
                enemy=CastleGuard(enemy,row);
            }
            else {var resident=Bots.All().First();enemy=CompetitiveProfile(MakeCompetitiveOffer(p,resident,Math.Min(2,CompetitiveBaseGrade(p,18)-1),2));}
            BeginCompetitive(p,18,enemy,[],mapId,spotId);c.Action=intrusion?1u:spot!.Status is 2 or 3?2u:0u;
            var info=CompetitiveEnemy(enemy);
            var encounter=new LocalCastleEncounterReply{enemyInfo=info,stamp=c.Stamp};encounter.cards.AddRange(enemy.Deck.Select((seq,i)=>RankingCard(enemy,seq,(uint)i)));return ok(encounter);
        }
        if(request is GsUpdateCastleDefenceDeckRequest defence) {
            var slots=new[]{defence.cardSlot1,defence.cardSlot2,defence.cardSlot3,defence.cardSlot4,defence.cardSlot5};
            if(spot==null||spot.Status!=1)return fail("castle defence requires owned occupied castle",ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO);
            if(defence.mercenayDataList.Count>0||defence.guildMercenaryDataList.Count>0)return fail("castle defence mercenary unavailable; select owned cards",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(CastleRosterError(p,slots,false,out var cards) is {} error)return fail("castle defence roster invalid: cards="+string.Join(',',slots),error);
            if(!CastleRosterAllowed(p,Competitive.Castles.Single(x=>x.Id==row!.CastleId),cards))return fail("castle native roster restrictions",ServerErrorCode.ERROR_INVALID_CARD_FACTION);
            spot.Defence=slots.ToList();return ok(new GsUpdateCastleDefenceDeckOk{coinInfo=Coins(p)});
        }
        if(request is GsUpdateCastleBattleResultRequest end) {
            if(c.CastleStamped&&!stampedResult)return fail("castle settlement requires its original battle token",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(row==null||c.ActiveMode!=18||c.Map!=mapId||c.Spot!=spotId||c.Started==instantResult||c.Action!=(uint)end.actionType||end.cashSkip!=instantResult||!Enum.IsDefined(end.battleResult))return Bad("castle result has no matching encounter");
            bool won=end.battleResult==CastleBattleResultType.CASTLE_BATTLE_RESULT_WIN,attack=end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_ATTACK;
            var slots=new[]{end.cardSlot1,end.cardSlot2,end.cardSlot3,end.cardSlot4,end.cardSlot5};
            if(CastleRosterError(p,slots,attack,out var cards) is {} rosterError)return fail("castle result roster invalid",rosterError);
            if((!instantResult&&(c.ReplayWinner!=(won?0u:1u)||!cards.SequenceEqual(c.Cards)))||end.deadCardSeqList.Any(x=>!cards.Contains(x)))return fail("castle result disagrees with the completed battle",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            var castle=Competitive.Castles.Single(x=>x.Id==row.CastleId);
            if(instantResult&&(!won||end.deadCardSeqList.Count>0||end.defenceDeckDeadCardSlotIndexList.Count>0||c.Action>2||!CastleRosterAllowed(p,castle,cards)))return fail("invalid instant castle roster or action",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            if(attack&&!c.CastleAdmissionPaid&&c.CastleAttacks>=catalog.VipFor(p.VipExp).CastleAttackCount&&c.CastlePaidLeft==0)return fail("no castle attacks left",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var kinds=new[]{BscTable.REWARD_TYPE.REWARD_TYPE_GOLD,BscTable.REWARD_TYPE.REWARD_TYPE_CASH,BscTable.REWARD_TYPE.REWARD_TYPE_STAMINA};
            var costs=CastleAdmissionCost(p,castle,c.Action);
            if(c.CastleAdmissionPaid)costs=[0,0,0];
            if(instantResult){
                uint count=c.CastleSkips.GetValueOrDefault(c.Action);
                if(count>=int.MaxValue)return fail("instant castle daily count exhausted",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                var weight=c.Action==0?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_SKIP_CASTLE_BATTLE_OCCUPY:c.Action==1?BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_SKIP_CASTLE_BATTLE_ATTACK:BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_SKIP_CASTLE_BATTLE_RESTORE;
                var price=catalog.Expenses.FindItem(weight,(int)count+1,true);
                if(price==null||price.Value<0||price.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_CASH||costs.Any(x=>x<0)||(long)costs[1]+price.Value>int.MaxValue)return Bad("invalid original instant castle price");
                costs[1]+=price.Value;
            }
            if(p.Gold<costs[0])return fail("not enough castle battle gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            if(p.CashPoint<costs[1])return fail("not enough diamonds for instant castle action",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            if(p.Stamina<costs[2])return fail("not enough castle battle stamina",ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
            uint beforeGold=p.Gold,beforeCash=p.CashPoint,beforeArena=p.ArenaPoint;
            uint earnedExp=won?(uint)Math.Max(0,attack?castle.PlunderRewardExp:end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY?castle.FirstRewardExp:castle.RestoreRewardExp):0;
            uint cardExp=won?(uint)Math.Max(0,attack?castle.PlunderCardRewardExp:end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY?castle.FirstCardRewardExp:castle.RestoreCardRewardExp):0;
            var growth=StageGrowth(p,earnedExp,(uint)costs[2]);if(growth==null)return Bad("castle EXP capacity exceeded");
            var earnedItems=new List<PKUserItem>();var earnedCards=new List<PKUserCardInfo>();
            if(won){var types=attack?castle.PlunderRewardValueIdType:end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY?castle.FirstRewardValueIdType:castle.RestoreRewardValueIdType;var ids=attack?castle.PlunderRewardValueId:end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY?castle.FirstRewardValueId:castle.RestoreRewardValueId;var quantities=attack?castle.PlunderRewardQuantity:end.actionType==CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY?castle.FirstRewardQuantity:castle.RestoreRewardQuantity;if(!GrantCompetitiveRewards(p,types,ids,quantities,out earnedItems,out earnedCards))return Bad("castle reward capacity or original reward unavailable");}
            uint gainedGold=p.Gold-beforeGold,gainedCash=p.CashPoint-beforeCash,gainedArena=p.ArenaPoint-beforeArena;PayCompetitiveCost(p,kinds,[0,0,0],costs);p.Exp=growth.Value.Exp;p.Level=growth.Value.Level;p.Stamina=growth.Value.Stamina;
            if(attack){RecordObjective(p,BscTable.CHECK_TYPE.MCT_ATTACK_CASTLE_BATTLE);if(won)RecordObjective(p,BscTable.CHECK_TYPE.MCT_WIN_ATTACK_CASTLE_BATTLE);if(!c.CastleAdmissionPaid){c.CastleAttacks++;c.CastleTargetAttacks++;if(c.CastleAttacks>catalog.VipFor(p.VipExp).CastleAttackCount)c.CastlePaidLeft--;}}
            if(!attack&&won){RecordObjective(p,BscTable.CHECK_TYPE.MCT_OCCUPY_CASTLE);spot!.Status=1;spot.WealthStamp=Now;spot.Defence=cards;}
            if(attack)foreach(ulong dead in end.deadCardSeqList)c.CastleDeadCards.Add(dead);
            var history=new PKCastleBattleHistoryData{battleTime=Now,battleResult=end.battleResult,enemyUid=c.Opponent,castleId=(uint)row.CastleId,isMyCastle=!attack,replaySeq=instantResult?0:c.Stamp,historySeq=c.Stamp,enemycommonInfo=new(){nick="本地城池守军",level=p.Level}};c.CastleHistory.Insert(0,WireCodec.Serialize(history));if(c.CastleHistory.Count>50)c.CastleHistory.RemoveAt(50);
            var reward=new PKCastleBattleRewardInfo{coinInfo=Coins(p),earnedCoinInfo=new(){gold=gainedGold,cashPoint=gainedCash,arenaPoint=gainedArena},earnedExp=earnedExp,exp=p.Exp,level=p.Level,stamina=p.Stamina,staminaStamp=p.StaminaStamp};foreach(ulong seq in cards){var state=CardState(p,seq);var card=catalog.RequireCard(p.CardIds[(int)seq-1]);int max=ProgressionRules.CardMax(catalog,p,card);state.Exp=(uint)Math.Min(ProgressionRules.CardCeiling(catalog,max),(ulong)state.Exp+cardExp);state.Level=ProgressionRules.CardLevel(catalog,state.Exp,max);reward.growUpCardList.Add(new(){cardSeq=seq,cardExp=state.Exp});}reward.earnedItemList.AddRange(earnedItems);reward.earnedCardList.AddRange(earnedCards);
            if(attack&&won)c.CastleTargetLooted.Add(mapId+"/"+spotId);
            if(instantResult)c.CastleSkips[c.Action]=c.CastleSkips.GetValueOrDefault(c.Action)+1;
            else c.CastleDifficulty.Record(won);c.Offers.Remove(18);
            var upgrades=!attack&&won?UpgradeLinkedCastles(p,home):[];
            var result=new GsUpdateCastleBattleResultOk{castleBattleRewardInfo=reward,castleMapSpotInfo=attack?new(){mapId=mapId,spotId=spotId,castleId=(uint)row.CastleId,spotStatus=won?CastleBattleStatus.CASTLE_BATTLE_STATUS_ATTACKED:CastleBattleStatus.CASTLE_BATTLE_STATUS_OCCUPY}:CastleSpotInfo(p,spot!),castleMapSpotDetailInfo=attack?new(){mapId=mapId,spotId=spotId}:CastleDetail(spot!),castleBattlePlayInfo=CastlePlay(p),castleAttackTryTimes=c.CastleTargetAttacks,battleEndSyncData=BallSync(p)};
            result.upGradeMapSpotInfoList.AddRange(upgrades);
            ClearCompetitive(p);return ok(result);
        }
        if(request is GsExchangeWealthPointRequest or GsBatchExchangeWealthPointRequest) {
            var targets=request is GsBatchExchangeWealthPointRequest batch?batch.batchList.Select(x=>(x.mapId,x.spotId)).ToArray():[(mapId,spotId)];
            if(targets.Length is 0 or >100||targets.Any(x=>x.Item1!=home.MapId)||targets.Distinct().Count()!=targets.Length)return Bad("invalid castle harvest list");var chosen=targets.Select(x=>CastleSpot(p,x.Item1,x.Item2)).ToArray();if(chosen.Any(x=>x==null||x.Status!=1))return Bad("harvest requires owned occupied castles");
            if(c.CastleExchanges>=catalog.VipFor(p.VipExp).WealthPointExchangeCount&&c.CastleExchangePaidLeft==0)return fail("no castle harvest entries left",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            foreach(var s in chosen)AccumulateCastleWealth(s!,Competitive.Castles.Single(x=>x.Id==home.SpotList.Single(tile=>tile.SpotId==s!.Spot).CastleId));
            ulong value=(ulong)chosen.Sum(x=>(long)x!.Wealth)*(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.CASTLE_WEALTH_EXCHANGE_GOLD_RATIO]);if((ulong)p.Gold+value>uint.MaxValue)return Bad("gold capacity exceeded");if(value==0)return Bad("no castle wealth accumulated");
            p.Gold+=(uint)value;c.CastleExchanges++;if(c.CastleExchanges>catalog.VipFor(p.VipExp).WealthPointExchangeCount)c.CastleExchangePaidLeft--;c.CastleExchangeWealth+=(uint)value;
            foreach(var harvested in chosen.OfType<LocalCastleSpot>().Where(x=>x.Wealth>0))
            {
                var tile=home.SpotList.Single(x=>x.SpotId==harvested.Spot);
                RotateCastleStore(p,harvested,tile);
                RotateCastleStore(p,harvested,tile,true);
                harvested.Wealth=0;
                harvested.WealthStamp=Now;
            }
            if(request is GsBatchExchangeWealthPointRequest)
            {
                var result=new GsBatchExchangeWealthPointOk{coinInfo=Coins(p),earnedcoinInfo=new(){gold=(uint)value},castleBattlePlayInfo=CastlePlay(p)};
                // The native harvest callback replaces the entire map cache.
                result.castleBattleMapSpotInfoList.AddRange(c.Castles.Values.Where(x=>x.Map==home.MapId).Select(x=>CastleSpotInfo(p,x)));
                return ok(result);
            }
            return ok(new GsExchangeWealthPointOk{coinInfo=Coins(p),earnedcoinInfo=new(){gold=(uint)value},castleBattleMapSpotInfo=CastleSpotInfo(p,spot!),castleMapSpotDetailInfo=CastleDetail(spot!),castleBattlePlayInfo=CastlePlay(p)});
        }
        return Bad("unsupported local castle operation");
    }
}
