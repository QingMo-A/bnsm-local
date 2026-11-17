using System.Text.Json;
using System.Security.Cryptography;

internal static class CastleInvasionRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","castle-invasion-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);}
        await using(var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(r);var p=r.Store.All().Single();var tables=new CompetitiveCatalog(root);var map=tables.CastleMaps.First();uint mid=(uint)map.MapId;p.Competitive.HomeMap=mid;
            foreach(var tile in map.SpotList.Where(t=>tables.Castles.Single(c=>c.Id==t.CastleId).BossId==0))p.Competitive.Castles[mid+"/"+tile.SpotId]=new(){Map=mid,Spot=(uint)tile.SpotId,Status=1,WealthStamp=ProtocolRouter.Now};
            using var peer=new Peer(root);await peer.Login(r,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.ActiveStage,p.ActiveBattleStamp,p.Competitive,p.Inventory,p.CardStates});
            async Task Bad(GameServerCommand cmd,object req,ServerErrorCode code,string phrase,string label){string before=State();var reply=await peer.Request<FailPacket>(cmd,req,success:false);Check(reply.errorCode==(uint)code&&reply.message.Contains(phrase),label+" gives specific Chinese feedback");Check(State()==before,label+" leaves costs, target, tokens, casualties and ownership unchanged");}
            PKCastleBattleMapSpotInfo? available=null;
            foreach(uint level in new[]{35u,65u,99u}){
                p.Level=level;p.Competitive.Offers.Remove(18);
                var search=await peer.Request<GsGetCastleBattleAttackMatchOk>(GameServerCommand.GS_GET_CASTLE_ATTACK_MATCH_USER_REQUEST,new GsGetCastleBattleAttackMatchRequest());
                Check(search.castleBattleMapSpotInfoList.Count==28,"All authored tile identities retained at player level "+level);
                uint ownerLevel=search.targetUserInfo.level;
                var occupied=search.castleBattleMapSpotInfoList.Where(s=>s.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_OCCUPY).ToArray();
                Check(occupied.Length>0,"Search has an eligible occupied castle at player level "+level);
                Check(occupied.All(s=>{var tile=map.SpotList.Single(t=>t.SpotId==s.spotId);return tile.RequirePcLevel<=ownerLevel&&tables.Castles.Single(c=>c.Id==tile.CastleId).BossId==0;}),"Bot never owns level-locked castles or monster tiles at player level "+level);
                foreach(var spot in occupied){
                    string before=State();var defence=await peer.Request<GsGetTargetCastleDefenceDeckInfoOk>(GameServerCommand.GS_GET_TARGET_CASTLE_DEFENCE_DECK_INFO_REQUEST,new GsGetTargetCastleDefenceDeckInfoRequest{mapId=mid,spotId=spot.spotId});
                    var castle=tables.Castles.Single(c=>c.Id==spot.castleId);
                    Check(defence.defenceDeckCardInfoList.Count>0&&defence.defenceDeckCardInfoList.All(packet=>{var card=r.Catalog.Cards.Single(c=>c.Id==packet.cardId);return (castle.FactionCond==BscTable.CARD_FACTION.FACTION_ALL||card.Faction==castle.FactionCond)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_FORCE||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE)&&(castle.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_PHYSICAL||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_PHYSICAL);}),"Bot defence respects original castle admission: level="+level+" spot="+spot.spotId);
                    Check(State()==before,"Defence preview never changes encounter: level="+level+" spot="+spot.spotId);
                }
                var unowned=search.castleBattleMapSpotInfoList.First(s=>s.spotStatus==CastleBattleStatus.CASTLE_BATTLE_STATUS_NORMAL);
                await Bad(GameServerCommand.GS_GET_TARGET_CASTLE_DEFENCE_DECK_INFO_REQUEST,new GsGetTargetCastleDefenceDeckInfoRequest{mapId=mid,spotId=unowned.spotId},ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO,"未被","Unoccupied bot tile");available=occupied.First();
            }
            var high=map.SpotList.First(t=>t.RequirePcLevel>65&&tables.Castles.Single(c=>c.Id==t.CastleId).BossId==0);p.Level=65;
            await Bad(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=(uint)high.SpotId,myCastleFlag=false},ServerErrorCode.ERROR_INVALID_USER_LEVEL,high.RequirePcLevel+"级","Locked invasion");p.Level=99;
            var tileRow=map.SpotList.Single(t=>t.SpotId==available!.spotId);var castleRow=tables.Castles.Single(c=>c.Id==tileRow.CastleId);
            bool Allowed(ulong seq){var card=r.Catalog.Cards.Single(c=>c.Id==p.CardIds[(int)seq-1]);return (castleRow.FactionCond==BscTable.CARD_FACTION.FACTION_ALL||card.Faction==castleRow.FactionCond)&&(castleRow.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_FORCE||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE)&&(castleRow.AttackTypeCond!=BscTable.CASTLE_ATTACK_TYPE.CASTLE_ATTACK_TYPE_PHYSICAL||card.AttackType==BscTable.ATTACK_TYPE.ATTACK_PHYSICAL);}
            ulong cardSeq=Enumerable.Range(1,p.CardIds.Count).Select(i=>(ulong)i).First(Allowed);
            var encounter=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=available!.spotId,myCastleFlag=false});
            LocalRankCombatRequest Start(params ulong[] cards){var req=new LocalRankCombatRequest{operation=4,mode=18,stamp=encounter.stamp};req.cards.AddRange(cards);return req;}
            await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(),ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO,"卡组无效","Empty selected deck");
            await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(cardSeq,cardSeq),ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO,"卡组无效","Duplicate selected card");
            await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(ulong.MaxValue),ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE,"卡组无效","Missing selected card");
            p.TrainingSlots[0]=new(){CardSequence=cardSeq,EndTime=ProtocolRouter.Now+60};await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(cardSeq),ServerErrorCode.ERROR_CARD_IN_TRAINING,"修炼","Training selected card");p.TrainingSlots.Clear();
            p.Competitive.CastleDeadCards.Add(cardSeq);await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(cardSeq),ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_CARD,"恢复","Fallen invasion card");
            var recovery=await peer.Request<GsBuyCastleBattleDeadCardRecoveryOk>(GameServerCommand.GS_BUY_CASTLE_BATTLE_DEAD_CARD_RECOVERY_REQUEST,new GsBuyCastleBattleDeadCardRecoveryRequest());
            Check(p.Competitive.CastleDeadCards.Count==0,"Original paid recovery clears invasion casualties without replacing prepared encounter");
            var battle=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(cardSeq));Check(battle.stamp==encounter.stamp&&p.Competitive.Started,"Valid start after roster rejection and recovery needs no restart");
            await Bad(GameServerCommand.GS_GET_ATTACKED_CASTLE_INFO_REQUEST,new GsGetAttackedCastleInfoRequest{mapId=mid,spotId=available.spotId,myCastleFlag=false},ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS,"上一场","Prepare while battle active");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=12,mode=18,stamp=battle.stamp});
            p.Competitive.CastleDeadCards.Add(cardSeq);var own=p.Competitive.Castles[mid+"/"+available.spotId];own.Status=1;
            await peer.Request<GsUpdateCastleDefenceDeckOk>(GameServerCommand.GS_UPDATE_CASTLE_DEFENCE_DECK_REQUEST,new GsUpdateCastleDefenceDeckRequest{mapId=mid,spotId=available.spotId,cardSlot1=cardSeq});
            Check(own.Defence.Where(x=>x!=0).SequenceEqual(new[]{cardSeq})&&p.Competitive.CastleDeadCards.Contains(cardSeq),"Invasion casualties do not prevent own castle defence or silently revive the card");
            own.Status=0;var occupy=await peer.Request<LocalCastleEncounterReply>(GameServerCommand.GS_CASTLE_BATTLE_OCCUPY_MATCH_REQUEST,new GsCastleBattleOccupyMatchRequest{mapId=mid,spotId=available.spotId});encounter=occupy;
            var startedOwn=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,Start(cardSeq));Check(p.Competitive.Started&&p.Competitive.Action==0&&p.Competitive.CastleDeadCards.Contains(cardSeq),"Own occupation follows native availability without erasing invasion casualties");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=12,mode=18,stamp=startedOwn.stamp});
            Check(p.ActiveStage==null&&p.Competitive.CastleHistory.Count==0,"Both actual retreats leave idle state without fake defeats or ownership");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/castle-invasion-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("CASTLE_INVASION_TEST_OK "+checks.Count);
    }
}
