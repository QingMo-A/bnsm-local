using System.Security.Cryptography;
using System.Text.Json;

internal static class CastleExploreInstantRegression
{
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);
        if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        p.Competitive.HomeMap=(uint)new CompetitiveCatalog(root).CastleMaps.First(x=>x.Prob>0).MapId;runtime.Store.Save();
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
        await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","castle-instant-server-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool pass,string message){if(!pass)throw new InvalidDataException(message);checks.Add(message);Console.WriteLine("PASS "+message);}
        uint mapId=0,spotId=0,savedSkips=0;ulong retired=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();var tables=new CompetitiveCatalog(root);var catalog=runtime.Catalog;
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.Exp,p.Level,p.ActiveStage,p.ActiveBattleStamp,p.Competitive,p.Inventory,p.CardStates,p.EquipmentItems});
            async Task Bad(object request,GameServerCommand cmd,string message){string before=State();await peer.Request<FailPacket>(cmd,request,success:false);Check(State()==before,message);}
            foreach(var map in tables.CastleMaps.Where(x=>x.Prob>0)){
                p.Competitive.HomeMap=(uint)map.MapId;p.Competitive.Castles.Clear();
                var roots=map.SpotList.Where(x=>x.RequirePcLevel==35&&x.CastleId>0).ToArray();
                Check(roots.Length>0,"Map "+map.MapId+" has original level-35 starting hexes");
                var first=roots[0];var request=new GsSearchMapSpotRequest{mapId=(uint)map.MapId,spotId=(uint)first.SpotId};uint gold=p.Gold;
                var opened=await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,request);
                Check(opened.castleMapSpotInfo.spotStatus==(CastleBattleStatus)0&&p.Competitive.Castles.Count==1,"First exploration reveals an unoccupied castle on map "+map.MapId);
                string before=State();await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,request,retry:true);Check(State()==before,"Exploration retry cannot spend or open twice on map "+map.MapId);
                var near=map.SpotList.FirstOrDefault(x=>x.CastleId>0&&x.SpotId!=first.SpotId&&LocalCastleMapRules.Neighbours(x.SpotId).Contains(first.SpotId));
                if(near!=null){
                    await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,new GsSearchMapSpotRequest{mapId=(uint)map.MapId,spotId=(uint)near.SpotId});
                    Check(p.Competitive.Castles.Count==2&&p.Competitive.Castles.Values.All(x=>x.Status==0),"A visible geometric neighbour opens while all previous castles remain unoccupied on map "+map.MapId);
                }
                foreach(var next in roots.Skip(1).Where(x=>!p.Competitive.Castles.ContainsKey(map.MapId+"/"+x.SpotId))){
                    await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,new GsSearchMapSpotRequest{mapId=(uint)map.MapId,spotId=(uint)next.SpotId});
                    Check(p.Competitive.Castles.ContainsKey(map.MapId+"/"+next.SpotId),"Other native starting regions remain explorable without an authored bridge on map "+map.MapId);
                }
                var remote=map.SpotList.FirstOrDefault(x=>x.CastleId>0&&x.RequirePcLevel!=35&&!p.Competitive.Castles.ContainsKey(map.MapId+"/"+x.SpotId)&&!LocalCastleMapRules.Neighbours(x.SpotId).Any(id=>p.Competitive.Castles.ContainsKey(map.MapId+"/"+id)));
                if(remote!=null)await Bad(new GsSearchMapSpotRequest{mapId=(uint)map.MapId,spotId=(uint)remote.SpotId},GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,"Unrevealed remote hex rejects without state changes on map "+map.MapId);
                await Bad(new GsSearchMapSpotRequest{mapId=(uint)map.MapId,spotId=(uint)first.SpotId},GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,"Fresh duplicate exploration cannot charge twice on map "+map.MapId);
            }
            var home=tables.CastleMaps.First(x=>x.Prob>0);p.Competitive.HomeMap=(uint)home.MapId;p.Competitive.Castles.Clear();
            var tile=home.SpotList.First(x=>x.RequirePcLevel==35&&x.CastleId>0);mapId=(uint)home.MapId;spotId=(uint)tile.SpotId;
            await peer.Request<GsSearchMapSpotOk>(GameServerCommand.GS_SEARCH_MAP_SPOT_REQUEST,new GsSearchMapSpotRequest{mapId=mapId,spotId=spotId});
            var castle=tables.Castles.Single(x=>x.Id==tile.CastleId);
            var slots=Enumerable.Range(1,p.CardIds.Count).Select(x=>(ulong)x).Where(seq=>{var card=catalog.Cards.Single(x=>x.Id==p.CardIds[(int)seq-1]);return (castle.FactionCond==BscTable.CARD_FACTION.FACTION_ALL||card.Faction==castle.FactionCond)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_FORCE||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_PHYSICAL||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_PHYSICAL);}).Take(5).ToArray();
            async Task<LocalCastleEncounterReply> Prepare()=>await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_CASTLE_BATTLE_OCCUPY_MATCH_REQUEST,new GsCastleBattleOccupyMatchRequest{mapId=mapId,spotId=spotId});
            LocalRankCombatRequest Instant(ulong stamp,uint action=0)=>new(){operation=14,mode=18,stamp=stamp,castleResult=new(){mapId=mapId,spotId=spotId,actionType=(CastleBattleActionType)action,cashSkip=true,battleResult=CastleBattleResultType.CASTLE_BATTLE_RESULT_WIN,cardSlot1=slots[0],cardSlot2=slots.ElementAtOrDefault(1),cardSlot3=slots.ElementAtOrDefault(2),cardSlot4=slots.ElementAtOrDefault(3),cardSlot5=slots.ElementAtOrDefault(4)}};
            var encounter=await Prepare();
            await Bad(Instant(0),GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"Instant occupation requires the exact selected encounter token");
            var invalid=Instant(encounter.stamp);invalid.castleResult.cardSlot2=invalid.castleResult.cardSlot1;
            await Bad(invalid,GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"Invalid instant roster rejects without payment or dangling battle");
            var price=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_SKIP_CASTLE_BATTLE_OCCUPY,1,true)!;
            uint cash=p.CashPoint;p.CashPoint=0;await Bad(Instant(encounter.stamp),GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"Insufficient diamonds preserves the prepared encounter and all balances");p.CashPoint=cash;
            var unpaid=Instant(encounter.stamp);unpaid.castleResult.cashSkip=false;await Bad(unpaid,GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"An instant request cannot claim the normal replay path for free");
            uint ammo=p.EquipmentItems.Aggregate(0u,(n,x)=>n+x.BsBallCount);uint attacks=p.Competitive.CastleAttacks;
            var requestOk=Instant(encounter.stamp);var done=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,requestOk);
            Check(p.CashPoint==cash-(uint)(castle.FirstConsumeGem+price.Value)+done.castleResult.castleBattleRewardInfo.earnedCoinInfo.cashPoint,"Instant occupation charges the exact original diamond price once");
            Check(p.Competitive.Castles[mapId+"/"+spotId].Status==1&&done.castleResult.castleBattlePlayInfo.todayOccupyBattleSkipCount==1,"Instant occupation returns authoritative ownership and daily skip count");
            Check(p.ActiveStage==null&&p.ActiveBattleStamp==0&&p.Competitive.ActiveMode==0&&p.Competitive.Stamp==0,"Instant occupation finishes atomically without starting a real battle");
            Check(p.Competitive.CastleAttacks==attacks&&p.Competitive.Replays.Count==0&&p.Competitive.CastleHistory.Count==1&&p.EquipmentItems.Aggregate(0u,(n,x)=>n+x.BsBallCount)==ammo,"Paid occupation consumes no ammunition, invasion quota or fabricated replay");
            string state=State();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,requestOk,retry:true);Check(State()==state,"Lost instant acknowledgement returns the receipt without duplicating payment, rewards or ownership");
            await Bad(Instant(encounter.stamp),GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"A second instant packet cannot reuse a completed encounter");
            p.Competitive.Castles[mapId+"/"+spotId].Status=2;var restored=await Prepare();retired=restored.stamp;
            await Bad(requestOk,GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"An old instant token cannot settle a newly prepared restoration");
            state=State();await peer.Request<FailPacket>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,requestOk,retry:true,success:false);Check(State()==state,"A stale receipt cannot replace the newer restoration encounter");
            var restore=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Instant(restored.stamp,2));
            Check(restore.castleResult.castleBattlePlayInfo.todayRestoreBattleSkipCount==1&&p.Competitive.Castles[mapId+"/"+spotId].Status==1&&p.ActiveStage==null,"Instant restoration uses its separate original price/count and leaves the player idle");
            foreach(var own in home.SpotList.Where(x=>x.CastleId>0).Take((int)catalog.Numbers[BscTable.NUMBER_TYPE.CASTLE_ATTACK_CHECK_HAVE_CASTLE_COUNT]))p.Competitive.Castles[mapId+"/"+own.SpotId]=new(){Map=mapId,Spot=(uint)own.SpotId,Status=1,WealthStamp=ProtocolRouter.Now};
            await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,new GsGetCastleBattleAttackMatchRequest());
            var invade=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mapId,spotId=spotId,myCastleFlag=false});
            uint invadeCash=p.CashPoint,invadeAttacks=p.Competitive.CastleAttacks,ownStatus=p.Competitive.Castles[mapId+"/"+spotId].Status;
            uint tierBattles=p.Competitive.CastleDifficulty.Battles;
            var invasion=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Instant(invade.stamp,1));
            var invadePrice=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_SKIP_CASTLE_BATTLE_ATTACK,1,true)!;
            Check(p.CashPoint==invadeCash-(uint)(castle.PlunderConsumeGem+invadePrice.Value)+invasion.castleResult.castleBattleRewardInfo.earnedCoinInfo.cashPoint,"Instant invasion charges its distinct original diamond price");
            Check(invasion.castleResult.castleBattlePlayInfo.todayAttackBattleSkipCount==1&&p.Competitive.CastleAttacks==invadeAttacks+1&&p.Competitive.CastleTargetAttacks==1,"Instant invasion consumes one attack and returns matching canonical counters");
            Check(p.Competitive.Castles[mapId+"/"+spotId].Status==ownStatus&&p.Competitive.CastleTargetLooted.Contains(mapId+"/"+spotId)&&p.Competitive.CastleDifficulty.Battles==tierBattles,"Instant invasion changes only the enemy visit and does not promote actual battle difficulty");
            await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            p.Competitive.Castles[mapId+"/"+spotId].Status=2;var active=await Prepare();
            var start=new LocalRankCombatRequest{operation=4,mode=18,stamp=active.stamp};start.cards.AddRange(slots);
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,start);
            await Bad(Instant(active.stamp,2),GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,"Instant shortcut cannot replace or settle a real active battle");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=12,mode=18,stamp=active.stamp});
            Check(p.ActiveStage==null&&p.Competitive.CastleSkips.GetValueOrDefault(2u)==1,"Retreat after a rejected shortcut clears only the real attempt without another skip payment");
            var finalRestore=await Prepare();await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Instant(finalRestore.stamp,2));
            Check(p.Competitive.CastleSkips.GetValueOrDefault(2u)==2,"The same castle can be restored after an acknowledged retreat with the next original daily price");
            savedSkips=p.Competitive.CastleSkips.GetValueOrDefault(0u);runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.All().Single();using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            var map=await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            Check(map.castleBattlePlayInfo.todayOccupyBattleSkipCount==savedSkips&&map.castleBattlePlayInfo.todayRestoreBattleSkipCount==2&&map.castleBattlePlayInfo.todayAttackBattleSkipCount==1&&map.castleBattleMapSpotInfoList.Any(x=>x.spotId==spotId&&x.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_OCCUPY),"Restart preserves instant counts, occupied castles and authoritative map state");
            Check(p.ActiveStage==null&&p.Competitive.ActiveMode==0,"Restart after an instant action has no unfinished battle");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/castle-explore-instant-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,normalSaveUsed=false,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()}));
        Console.WriteLine("CASTLE_EXPLORE_INSTANT_PASSED "+checks.Count);
    }
}
