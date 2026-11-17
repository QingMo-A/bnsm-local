using System.Text.Json;
using System.IO.Compression;

internal static class CompetitiveRegression
{
    internal static void Prepare(LocalRuntime runtime)
    {
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;
        var bot=new LocalBotBuilder(c).Build(999002,"Fixture","legend","balanced",817,ProtocolRouter.Now).Profile;
        p.CardIds=bot.CardIds;p.CardStates=bot.CardStates;p.EquipmentItems=bot.EquipmentItems;p.NextEquipmentSequence=bot.NextEquipmentSequence;p.Inventory=bot.Inventory;p.Deck=bot.Deck;
        foreach(var row in c.UsableHeroes.Where(x=>x.Rank==8&&!LocalHongCard.IsCard(x.Id)).GroupBy(x=>x.EvolveFirstAncestor).Select(g=>g.First()).Take(20)) {
            p.CardIds.Add((uint)row.Id);p.CardStates[(ulong)p.CardIds.Count]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=5,ActiveSkills=[99,99],PassiveSkills=[99,99,99,99,99]};
        }
        foreach(uint id in new[]{8u,50u,52u,54u,33u})p.Decks[id]=p.Deck.ToList();
        p.Decks[56]=Enumerable.Range(1,15).Select(x=>(ulong)x).ToList();p.Decks[57]=p.Decks[56].ToList();
        for(uint deck=34;deck<=36;deck++)p.Decks[deck]=Enumerable.Range((int)(deck-34)*5+1,5).Select(x=>(ulong)x).ToList();
        p.Inventory[800000001]=1000;p.Gold=100000000;p.CashPoint=1000000;p.Stamina=10000;p.StaminaStamp=ProtocolRouter.Now;
        p.ClearStage=c.Stages.Keys.Max();p.AttendanceLastDay=p.ShortTermLastDay=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=p.ShortTermDays=1;p.AttendanceLastTime=ProtocolRouter.Now;p.AttendanceVipClaimedDay=p.AttendanceLastDay;
        runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);if(runtime.Store.All().Count==0){Prepare(runtime);var p=runtime.Store.All().Single();p.Competitive.HomeMap=1;p.Competitive.Castles["1/1"]=new(){Map=1,Spot=1,WealthStamp=ProtocolRouter.Now};
            if(Environment.GetEnvironmentVariable("BNSM_SHOP_TEST_FUNDS")=="1")p.WorldBossPoint=100000000;
            if(uint.TryParse(Environment.GetEnvironmentVariable("BNSM_LADDER_TEST_RANK"),out uint rank)&&rank>0)foreach(uint mode in new uint[]{10,12,31,42})p.Competitive.Records[mode]=new(){Battles=150,Wins=80,Rank=rank,BestRank=rank};
            string? worldState=Environment.GetEnvironmentVariable("BNSM_WORLD_TEST_STATE");
            if(worldState is "empty" or "dead"){
                runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_ON]=runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_OFF]=0;
                if(worldState=="empty")p.Decks[33]=Enumerable.Repeat(0UL,5).ToList();
                else p.Competitive.WorldCards[p.Decks[33][0]]=new(){Hp=0};
            }
            runtime.Store.Save();}
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","competitive-"+Guid.NewGuid().ToString("N"));var checks=new List<string>();
        void Check(bool pass,string label){if(!pass)throw new InvalidDataException(label);checks.Add(label);}
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.ArenaPoint,p.Stamina,p.Inventory,p.EquipmentItems,p.Competitive});
            async Task Replay(uint mode,uint winner=0) {
                var state=p.Competitive;var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),state.Enemy)!;
                if(state.ActiveMode==18)state.Cards=p.Decks[54].Where(x=>x!=0).ToList();
                var data=new PKBattleReplayData{gameMode=(BscTable.GAME_MODE)mode,winTeamIndex=winner};data.thumbNailInfo.Add(new(){uid=p.Uid});data.thumbNailInfo.Add(new(){uid=state.Opponent});
                uint slot=0;foreach(var seq in state.Cards){uint card=p.CardIds[(int)seq-1];if(state.ActiveMode==42)card=(uint)new CompetitiveCatalog(root).BalanceCards.Single(x=>c.Cards.Single(row=>row.Id==x.CardId).EvolveFirstAncestor==c.Cards.Single(row=>row.Id==card).EvolveFirstAncestor).CardId;data.battleCardInfo.Add(new(){teamIndex=0,cardId=card,cardSlotIndex=slot++,uid=p.Uid});}
                slot=0;foreach(var card in enemy.cardInfoList)data.battleCardInfo.Add(new(){teamIndex=1,cardId=card.cardId,cardSlotIndex=slot++,uid=state.Opponent});data.battleProcessInfo.Add(new());
                byte[] compressed;using(var output=new MemoryStream()){using(var z=new ZLibStream(output,CompressionLevel.Fastest,true))z.Write(WireCodec.Serialize(data));compressed=output.ToArray();}
                var request=new GsUploadReplayDataRequest{gameMode=(BscTable.GAME_MODE)mode,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)compressed.Length,replayData=compressed,firstSendDataFlag=true};
                await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,request);string before=State();await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,request,retry:true);Check(State()==before,"Replay upload retry is idempotent for mode "+mode);
            }
            ulong weapon=p.CardStates[1].WeaponSequence;
            var load=new GsEquipBSBallRequest{cardSeq=1,ItemSeq=weapon,bsBallId=800000001,bsBallCount=50};
            var equipped=await peer.Request<GsEquipBSBallOk>(GameServerCommand.GS_EQUIP_BSBALL_REQUEST,load);
            Check(p.Inventory[800000001]==950&&p.EquipmentItems.Single(x=>x.Sequence==weapon).BsBallCount==50,"Ammunition transfers from inventory into the equipped weapon");
            Check(equipped.deltaItemInfoList.Any(x=>x.itemSeq==weapon&&x.itemCount==0&&x.bsBallCount==50),"Weapon metadata delta preserves the weapon instance");
            string stateBefore=State();await peer.Request<GsEquipBSBallOk>(GameServerCommand.GS_EQUIP_BSBALL_REQUEST,load,retry:true);Check(State()==stateBefore,"Ammunition equip retry cannot consume twice");
            load.bsBallCount=9;stateBefore=State();await peer.Request<FailPacket>(GameServerCommand.GS_EQUIP_BSBALL_REQUEST,load,success:false);Check(State()==stateBefore,"Invalid ammunition quantity rejects atomically");
            await peer.Request<object>(GameServerCommand.GS_CHANGE_USE_BSBALL_FLAG_REQUEST,new GsChangeUseBSBallFlagRequest{bsBallUseFlag=true});
            var lobby=await peer.Request<GsEnterArenaBattleOk>(GameServerCommand.GS_ENTER_ARENA_BATTLE_REQUEST,new GsEnterArenaBattleRequest());
            var enemies=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            Check(enemies.enemyList.Count==9&&enemies.enemyList.Select(x=>x.enemyRankGrade).Distinct().Count()==3,"Native arena offers three opponent categories with persistent bot identities");
            uint opponent=enemies.enemyList[0].enemyUid;
            var deck=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=opponent});
            Check(deck.battleCardInfoList.Count==5&&deck.battleCardInfoList.All(x=>x.cardLevel<=p.Level&&x.cardEquipInfo.Count==6&&x.cardSkillEnchant.Count==7)&&p.Competitive.Offers[12].All(x=>x.Grade<=2),"Introductory opponents carry legal tiered cards and gear without forcing grandmaster equipment");
            Check(!p.Competitive.Records.ContainsKey(12)||p.Competitive.Records[12].Used==0,"Inspecting an opponent does not spend a ticket or ammunition");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=opponent});
            Check(p.Competitive.Records[12].Used==1&&p.EquipmentItems.Single(x=>x.Sequence==weapon).BsBallCount==40,"Starting arena consumes one ticket and one ten-shot ammunition charge");
            var end=new GsUpdateArenaBattleResultRequest{targetUid=opponent,battleResult=0,battleEndSyncData=new(),battleHistory=new()};end.useCardSeq.AddRange(p.Deck);
            await peer.Request<FailPacket>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end,success:false);Check(p.Competitive.Records[12].Battles==0,"Result cannot settle before the actual native replay is supplied");
            await Replay(12);var settled=await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end);
            Check(p.Competitive.Records[12].Wins==1&&p.Competitive.ActiveMode==0&&p.ActiveStage==null&&settled.battleEndSyncData.bsBallDeltaItemInfoList.Single(x=>x.itemSeq==weapon).bsBallCount==40,"Arena settles rank and authoritative ammunition without a second consumption");
            stateBefore=State();await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end,retry:true);Check(State()==stateBefore,"Arena result retry cannot duplicate rank or rewards");
            Check(p.Missions.Counters.GetValueOrDefault(BscTable.CHECK_TYPE.MCT_WIN_ARENA_PVP)==1,"Arena win counts toward native missions");
            Check(p.Activities.Weekly.GetValueOrDefault("MCT_ENTER_ARENA_PVP")==1,"One actual arena settlement and its retry produce exactly one restored activity credit");
            foreach(uint mode in new[]{10u,42u}) {
                var match=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=mode});
                Check(match.enemyCards.Count==5&&match.record.todayEntranceCount==1&&match.stamp>0,"Ranked mode "+mode+" matches through the game server without the retired online battle service");
                await Replay(12);
                var finish=new LocalRankCombatRequest{operation=2,mode=mode,stamp=match.stamp,result=0};finish.cards.AddRange(p.Deck);
                var result=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,finish);
                Check(result.record.totalBattleWinCount==1&&p.Competitive.ActiveMode==0,"Ranked mode "+mode+" saves the validated battle result");
                stateBefore=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,finish,retry:true);Check(State()==stateBefore,"Ranked mode "+mode+" settlement retry is idempotent");
                Check(p.Activities.Weekly.GetValueOrDefault(mode==10?"MCT_ENTER_RANKING_PVP":"MCT_ENTER_BALANCE_PVP")==1,"Ranked mode "+mode+" completes exactly one original activity objective");
            }
            var castle=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());uint home=castle.castleBattlePlayInfo.haveMapIdList.Single();
            var catalog=new CompetitiveCatalog(root);var tile=catalog.CastleMaps.Single(x=>x.MapId==home).SpotList.First(x=>x.CastleId>0);
            var explored=await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,new GsSearchMapSpotRequest{mapId=home,spotId=(uint)tile.SpotId});
            Check(explored.castleMapSpotInfo.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_NORMAL,"Castle exploration unlocks a real map tile");
            await peer.Request<GsCastleBattleOccupyMatchOk>(GameServerCommand.GS_CASTLE_BATTLE_OCCUPY_MATCH_REQUEST,new GsCastleBattleOccupyMatchRequest{mapId=home,spotId=(uint)tile.SpotId});var castleStart=new LocalRankCombatRequest{operation=4,mode=18};castleStart.cards.AddRange(p.Decks[54]);await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,castleStart);await Replay(18);
            var conquest=new GsUpdateCastleBattleResultRequest{mapId=home,spotId=(uint)tile.SpotId,actionType=CastleBattleActionType.CASTLE_BATTLE_ACTION_OCCUPY,battleResult=CastleBattleResultType.CASTLE_BATTLE_RESULT_WIN,cardSlot1=1,cardSlot2=2,cardSlot3=3,cardSlot4=4,cardSlot5=5,battleHistory=new(),battleEndSyncData=new()};
            uint stamina=p.Stamina;var conquered=await peer.Request<GsUpdateCastleBattleResultOk>(GameServerCommand.GS_UPDATE_CASTLE_BATTLE_RESULT_REQUEST,conquest);
            Check(conquered.castleMapSpotInfo.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_OCCUPY&&p.Stamina==stamina-catalog.Castles.Single(x=>x.Id==tile.CastleId).FirstConsumeStamina,"Castle victory grants ownership and charges the original battle cost once");
            stateBefore=State();await peer.Request<GsUpdateCastleBattleResultOk>(GameServerCommand.GS_UPDATE_CASTLE_BATTLE_RESULT_REQUEST,conquest,retry:true);Check(State()==stateBefore,"Castle result retry cannot duplicate ownership rewards");
            var owned=p.Competitive.Castles.Values.Single();owned.WealthStamp=ProtocolRouter.Now-120;uint gold=p.Gold;
            var harvest=await peer.Request<GsExchangeWealthPointOk>(GameServerCommand.GS_EXCHANGE_WEALTH_POINT_REQUEST,new GsExchangeWealthPointRequest{mapId=home,spotId=owned.Spot});
            Check(harvest.earnedcoinInfo.gold>0&&p.Gold==gold+harvest.earnedcoinInfo.gold&&owned.Wealth==0,"Occupied castle accrues and harvests the original wealth rate");
            // A fresh request cannot repeat first-conquest rewards after ownership changes.
            stateBefore=State();await peer.Request<FailPacket>(GameServerCommand.GS_CASTLE_BATTLE_OCCUPY_MATCH_REQUEST,new GsCastleBattleOccupyMatchRequest{mapId=home,spotId=owned.Spot},success:false);Check(State()==stateBefore,"Owned castles reject repeated first-conquest attempts atomically");
            var nativeShop=CastleShopTest();await nativeShop;
            async Task CastleShopTest(){
                int slot=0;uint id=harvest.castleBattleMapSpotInfo.storeInfoList[slot].storeGoodsId,beforeGold=p.Gold;var buy=new GsBuyCastleStoreGoodsRequest{mapId=home,spotId=owned.Spot,SlotIndex=(uint)slot,goodsId=id};
                Check(id!=conquered.castleMapSpotInfo.storeInfoList[slot].storeGoodsId,"Harvest rotates the castle's store immediately");
                var goods=c.Goods.Single(x=>x.GoodsId==id);uint cost=(uint)((ulong)goods.Gold*(uint)Math.Round((float)tile.MoneyFactor[slot]*100f)/100);
                var bought=await peer.Request<GsBuyCastleStoreGoodsOk>(GameServerCommand.GS_BUY_CASTLE_STORE_GOODS_REQUEST,buy);Check(p.Gold==beforeGold-cost&&bought.castleStoreInfo.storeBuyCount==1,"Owned castle shop charges its original discounted price and saves sold stock");
                string before=State();await peer.Request<GsBuyCastleStoreGoodsOk>(GameServerCommand.GS_BUY_CASTLE_STORE_GOODS_REQUEST,buy,retry:true);Check(State()==before,"Castle shop retry cannot duplicate goods or payment");
            }
            foreach(var pair in new[]{(Mode:10u,Kind:1),(Mode:12u,Kind:2),(Mode:31u,Kind:7),(Mode:42u,Kind:14)}) {
                p.VipExp=(uint)c.Vip.Max(x=>x.LvUpNeedVipExp);uint before=p.CashPoint;var buy=new GsBuyBonusCountRequest{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)pair.Kind,buyCount=1};
                var paid=await peer.Request<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,buy);Check(paid.changedValue==1&&p.Competitive.Records[pair.Mode].PaidLeft==1&&p.CashPoint<before,"Competitive mode "+pair.Mode+" buys a native paid entry and preserves the free quota");
                stateBefore=State();await peer.Request<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,buy,retry:true);Check(State()==stateBefore,"Paid competitive ticket retry is idempotent for mode "+pair.Mode);
            }
            var rewardRow=catalog.Rewards.First(x=>x.PvpBattleType==(BscTable.PVP_BATTLE_TYPE)9&&x.BattleResultType==BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_SCORE);p.Competitive.Records[31].RewardPoints=(uint)rewardRow.Score;
            var claim=new GsGetInfinitArenaTodayRewardRequest{rewardId=(uint)rewardRow.Id};await peer.Request<GsGetInfinitArenaTodayRewardOk>(GameServerCommand.GS_GET_INFINIT_ARENA_TODAY_REWARD_REQUEST,claim);Check(p.Competitive.Records[31].RewardClaims.Contains((uint)rewardRow.Id),"Infinite arena score reward records its original catalog claim");
            stateBefore=State();await peer.Request<GsGetInfinitArenaTodayRewardOk>(GameServerCommand.GS_GET_INFINIT_ARENA_TODAY_REWARD_REQUEST,claim,retry:true);Check(State()==stateBefore,"Infinite score reward retry cannot duplicate rewards");
            // This isolated protocol fixture tests settlement, not the real-clock daily break.
            // Keep it deterministic when CI happens to run during the original 22:00 break.
            runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_ON]=0;
            runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BREAKTIME_OFF]=0;
            var world=await peer.Request<GsGetWorldWarLobbyInfoOk>(GameServerCommand.GS_GET_WORLD_WAR_LOBBY_INFO_REQUEST,new GsGetWorldWarLobbyInfoRequest());
            Check(world.territoryInfoList.Count==catalog.Territories.Count(x=>x.MapOpen)&&world.territoryInfoList.All(x=>x.dailyRewardGroupId>0),"World war exposes all open original territories and real reward groups");
            var ready=await peer.Request<GsWorldWarReadyOk>(GameServerCommand.GS_WORLD_WAR_READY_REQUEST,new GsWorldWarReadyRequest{territoryId=6});
            Check(ready.enemyInfo.bAIBattle&&ready.enemyInfo.cardinfolist.Count==5&&ready.myCardStatusList.Count==5,"World war prepares a real bot opponent and persisted player HP slots");
            await peer.Request<object>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest());await Replay(44,1);
            var worldEnd=new GsWorldWarEndConfirmRequest{bWin=false,battleHistory=new(),battleEndSyncData=new()};for(ulong seq=1;seq<=5;seq++)worldEnd.myCardStatusList.Add(new(){cardSeq=seq,hpRatio=seq==1?0:.5f});
            var worldResult=await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,worldEnd);
            Check(worldResult.attackPoint==(uint)c.Numbers[BscTable.NUMBER_TYPE.WORLDWAR_BATTLE_ATTACK_LOSE]&&p.Competitive.WorldCards[1].Hp==0,"World war applies the original defeat score and preserves dead-card state");
            stateBefore=State();await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,worldEnd,retry:true);Check(State()==stateBefore,"World war retry cannot duplicate territory score");
            async Task<GsGetWorldWarRankerInfoListOk> Board(uint territory,WorldWarRankType type)=>await peer.Request<GsGetWorldWarRankerInfoListOk>(GameServerCommand.GS_GET_WORLD_WAR_RANKER_INFO_LIST_REQUEST,new GsGetWorldWarRankerInfoListRequest{territoryId=territory,rankType=type});
            var daily=await Board(6,WorldWarRankType.DAILY);
            Check(daily.rankerInfoList.Single().uid==p.Uid&&daily.myRankInfo.rank==1&&daily.myRankInfo.totalPoint==worldResult.attackPoint,"Daily board shows the authoritative territory score with no synthetic opponent ranks");
            var other=await Board(7,WorldWarRankType.DAILY);
            Check(other.myRankInfo.rank==0&&other.rankerInfoList.Count==0&&!p.Competitive.WorldScores.ContainsKey(7),"Reading an unplayed territory keeps it unranked without creating stored scores");
            p.Competitive.WorldDay--;
            daily=await Board(6,WorldWarRankType.DAILY);
            var weekly=await Board(6,WorldWarRankType.WEEKLY);
            Check(daily.rankerInfoList.Count==0&&weekly.myRankInfo.totalPoint==worldResult.attackPoint,"Offline daily rank expires independently of the weekly rank");
            p.Competitive.WorldDay++;
            await peer.Request<GsReviveWorldWarCardOk>(GameServerCommand.GS_REVIVE_WORLD_WAR_CARD_REQUEST,new GsReviveWorldWarCardRequest());Check(p.Competitive.WorldCards.Count==0&&p.Competitive.WorldRevives==1,"World war recovery restores cards and consumes its original daily quota");
            await peer.Request<GsWorldWarReadyOk>(GameServerCommand.GS_WORLD_WAR_READY_REQUEST,new GsWorldWarReadyRequest{territoryId=6});
            await peer.Request<object>(GameServerCommand.GS_WORLD_WAR_START_REQUEST,new GsWorldWarStartRequest());await Replay(44,0);
            var win=new GsWorldWarEndConfirmRequest{bWin=true,battleHistory=new(),battleEndSyncData=new()};
            foreach(var seq in p.Competitive.Cards){var native=c.Cards.Single(x=>x.Id==p.CardIds[(int)seq-1]);win.myCardStatusList.Add(new(){cardSeq=seq,hpRatio=.8f,spRatio=native.MaxSp>0?native.MaxSp:100});}
            float savedSp=win.myCardStatusList[0].spRatio;win.myCardStatusList[0].spRatio=float.NaN;stateBefore=State();
            await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,win,success:false);Check(State()==stateBefore,"World war rejects nonfinite inner-energy values before mutating survivors or score");
            win.myCardStatusList[0].spRatio=savedSp+1;await peer.Request<FailPacket>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,win,success:false);Check(State()==stateBefore,"World war rejects energy above that card's native maximum atomically");
            win.myCardStatusList[0].spRatio=savedSp;var won=await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,win);
            Check(p.Competitive.WorldCards[win.myCardStatusList[0].cardSeq].Sp==savedSp&&savedSp>1&&p.Competitive.WorldDifficulty.Wins==1,"A validated victory persists actual inner-energy points above one, with one difficulty win");
            stateBefore=State();await peer.Request<GsWorldWarEndConfirmOk>(GameServerCommand.GS_WORLD_WAR_END_CONFIRM_REQUEST,win,retry:true);Check(State()==stateBefore,"World war victory retry cannot duplicate score, survivor energy or difficulty progression");
            var links=tile.LinkSpot.Where(id=>catalog.Castles.Single(x=>x.Id==catalog.CastleMaps.Single(map=>map.MapId==home).SpotList.Single(row=>row.SpotId==id).CastleId).BossId==0).ToArray();
            Check(links.Length>0&&!owned.Upgraded,"An isolated castle remains at its original grade");
            foreach(int id in links)p.Competitive.Castles[home+"/"+id]=new(){Map=home,Spot=(uint)id,Status=1,WealthStamp=ProtocolRouter.Now};
            var upgraded=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            var upgradedTile=upgraded.castleBattleMapSpotInfoList.Single(x=>x.spotId==owned.Spot);
            Check(upgradedTile.upgradeFlag&&owned.Upgraded,"Occupying all authored bridges upgrades linked castles already in a save");
            var castleData=catalog.Castles.Single(x=>x.Id==tile.CastleId);
            owned.Wealth=0;owned.WealthStamp=ProtocolRouter.Now-120;
            var secondHarvest=await peer.Request<GsExchangeWealthPointOk>(GameServerCommand.GS_EXCHANGE_WEALTH_POINT_REQUEST,new GsExchangeWealthPointRequest{mapId=home,spotId=owned.Spot});
            Check(secondHarvest.earnedcoinInfo.gold==Math.Min(castleData.UpWealthPointDelta*2,castleData.UpWealthPointMax),"Upgraded castle accrues original bonus resources and harvest clears sold stock");
            Check(secondHarvest.castleBattleMapSpotInfo.storeInfoList.All(x=>x.storeBuyCount==0),"Harvest resets stock for the new rotation");
            var batchHarvest=new GsBatchExchangeWealthPointRequest();
            batchHarvest.batchList.Add(new(){mapId=home,spotId=owned.Spot});
            owned.Wealth=100;
            var batchResult=await peer.Request<GsBatchExchangeWealthPointOk>(GameServerCommand.GS_BATCH_EXCHANGE_WEALTH_POINT_REQUEST,batchHarvest);
            Check(batchResult.castleBattleMapSpotInfoList.Count==p.Competitive.Castles.Count,"Batch harvest returns the complete home map for the native cache");
            runtime.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var p=restarted.Store.All().Single();Check(p.BsBallUseFlag&&p.Competitive.Records[12].Wins==1&&p.Competitive.Records[10].Wins==1&&p.Competitive.Castles.Values.Any(x=>x.Status==1)&&p.Competitive.WorldScores[6].Daily>0,"Fresh server preserves ammunition, arena/ranked records, castle ownership and territory score");
        }
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/competitive-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=hash},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("COMPETITIVE_SERVER_PASSED "+checks.Count);
    }
    internal static void Audit(string root)
    {
        var c=new GameCatalog(root);var tables=new CompetitiveCatalog(root);
        var report=new{tables.Modes,tables.Rewards,tables.CastleMaps,tables.Castles,tables.Territories,tables.WorldRewards,tables.BalanceCards,
            numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("PVP")||x.Key.ToString().Contains("ARENA")||x.Key.ToString().Contains("CASTLE")||x.Key.ToString().Contains("WORLDWAR")||x.Key.ToString().Contains("BSBALL")).ToDictionary(x=>x.Key.ToString(),x=>x.Value),
            balls=c.Items.Where(x=>x.ItemType.ToString().Contains("BSBALL")),effects=tables.ItemEffects.Where(x=>c.Items.Any(i=>i.ItemType.ToString().Contains("BSBALL")&&i.Id==x.ItemId)),
            money=c.MoneyResources.Where(x=>x.MoneyEventType.ToString().Contains("BSBALL"))};
        File.WriteAllText("I:/BnsmModernWork/Verification/competitive-catalog-audit.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine("COMPETITIVE_CATALOG_READY");
    }
}
