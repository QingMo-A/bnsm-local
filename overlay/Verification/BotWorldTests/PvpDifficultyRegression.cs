using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;

internal static class PvpDifficultyRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","pvp-difficulty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var checks=new List<string>();var samples=new List<object>();
        void Check(bool value,string note){if(!value)throw new InvalidDataException(note);checks.Add(note);Console.WriteLine("PASS "+note);}
        uint persistedUid=0,persistedEliteUid=0;byte[] persistedPreview=[],persistedElite=[];string account="";
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();account=p.Account;
            string residents=JsonSerializer.Serialize(runtime.Router.Bots.All());
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            foreach(var stage in new (string Name,uint Battles,uint Wins,uint Rank,uint Losses,uint Level,int Base)[]{("new",0u,0u,10000u,0u,99u,1),("intermediate",30u,20u,8000u,0u,99u,2),("advanced",150u,80u,800u,0u,99u,3),("loss-relief",150u,80u,800u,3u,99u,2),("low-level",150u,80u,9000u,0u,40u,1),("mid-level",150u,80u,8000u,0u,80u,2)}){
                p.Level=stage.Level;p.Competitive.Records[12]=new(){Battles=stage.Battles,Wins=stage.Wins,Rank=stage.Rank,LossStreak=stage.Losses};p.Competitive.Offers.Remove(12);
                var list=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
                var offers=p.Competitive.Offers[12];
                Check(list.enemyList.Count==9&&offers.Select(x=>x.Uid).Distinct().Count()==9,stage.Name+": nine distinct resident identities");
                Check(offers.All(x=>x.Grade==Math.Max(stage.Base+(x.Badge==0?1:x.Badge==2?-1:0),stage.Battles<5&&stage.Rank>1000?0:x.Rank<=100?4:x.Rank<=300?3:x.Rank<=1000?2:x.Rank<=5000?1:0)),stage.Name+": strong/matched/easy grades retain the opponent rank floor");
                var means=new Dictionary<uint,double>();
                foreach(uint badge in new[]{2u,1u,0u}){
                    var enemy=list.enemyList.First(x=>x.enemyRankGrade==badge);
                    var deck=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=enemy.enemyUid});
                    var spec=offers.Single(x=>x.Uid==enemy.enemyUid);
                    Check(deck.battleCardInfoList.Count==5&&deck.battleCardInfoList.All(x=>x.cardLevel<=stage.Level&&x.cardSkillEnchant.Count==7&&x.cardEquipInfo.Count==6),stage.Name+"/"+badge+": complete legal levels, equipment and seven enchant slots");
                    Check(spec.Grade>1||deck.battleCardInfoList.All(x=>x.cardSkillEnchant.All(e=>e==0)),stage.Name+"/"+badge+": low tiers do not receive max skill enchant");
                    Check(stage.Level>=runtime.Catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SKILL_ENCHANT)||deck.battleCardInfoList.All(x=>x.cardSkillEnchant.All(e=>e==0)),stage.Name+"/"+badge+": original player-level skill-enchant gate is preserved");
                    means[badge]=deck.battleCardInfoList.Average(x=>x.cardSkillLevel.Sum(v=>(double)v));
                    samples.Add(new{stage=stage.Name,badge,spec.Grade,spec.Level,meanSkill=means[badge],meanStar=deck.battleCardInfoList.Average(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.cardId).Rank),maxSkillEnchant=deck.battleCardInfoList.Max(x=>x.cardSkillEnchant.Max())});
                }
                Check(means[2]<=means[1]&&means[1]<=means[0]&&means[2]<means[0],stage.Name+": actual skill budgets rise across the three displayed choices with rank floors");
            }
            // Rank belongs to the offered opponent, not merely the viewer's
            // level/win count. Exercise real packets across every ladder edge.
            foreach(uint rank in new uint[]{10000,5001,5000,1001,1000,301,300,101,100,31,30,11,10,2,1}){
                p.Level=99;p.Competitive.Records[12]=new(){Battles=150,Wins=80,Rank=rank};p.Competitive.Offers.Remove(12);
                var list=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
                foreach(var offer in p.Competitive.Offers[12]){
                    int floor=offer.Rank<=100?4:offer.Rank<=300?3:offer.Rank<=1000?2:offer.Rank<=5000?1:0;
                    Check(offer.LadderVersion==1&&offer.Grade>=floor,$"rank {rank}/{offer.Rank}: actual opponent meets its own ladder floor");
                    var deck=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=offer.Uid});
                    foreach(var card in deck.battleCardInfoList){
                        var data=runtime.Catalog.Cards.Single(x=>x.Id==card.cardId);
                        foreach(var item in card.cardEquipInfo){
                            var gear=runtime.Catalog.ItemsById[(int)item.itemID];
                            if(item.empowerEffect.Any(x=>x>0)){
                                Check(gear.Empower&&gear.EmpowerMaxCost>0&&item.empowerEffect.Count==EngravingRules.Attributes.Length,"Ladder engraving uses eligible native equipment and complete attribute slots");
                                int index=item.empowerEffect.FindIndex(x=>x>0);var type=EngravingRules.Attributes[index];
                                Check(runtime.Catalog.Items.Any(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&x.EmpowerValue>0&&x.EmpowerLimitItemLevel<=gear.Level&&x.AttributeValueIdType.Count==1&&x.AttributeValueIdType[0]==type&&x.AttributeValueId.Count==1&&x.AttributeValueId[0]>0&&
                                    item.empowerEffect[index]%(uint)x.AttributeValueId[0]==0&&(ulong)(item.empowerEffect[index]/(uint)x.AttributeValueId[0])*(uint)x.EmpowerValue<=(uint)(gear.EmpowerMaxCost*offer.GrowthStage/5)),"Ladder engraving can be reproduced with original stones inside its rank budget");
                            }
                        }
                        foreach(var effect in card.effectDataInfoList){
                            var rule=runtime.Catalog.Transcendence.Single(x=>x.CardId==card.cardId).EffectDataList.Single(x=>x.Id==effect.effectDataId);
                            Check(data.Rank>=runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.TRANSCENDENCE_CONTENT_ON]&&effect.effectGrade<=offer.GrowthStage&&effect.effectGrade<=rule.EffectGradeDataList.Count&&effect.effectDataValue==rule.EffectGradeDataList[(int)effect.effectGrade-1].EffectMax,"Ladder transcendence obeys original unlock, grade and value caps");
                        }
                    }
                    samples.Add(new{stage="ladder",viewerRank=rank,opponentRank=offer.Rank,offer.Grade,offer.GrowthStage,meanStar=deck.battleCardInfoList.Average(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.cardId).Rank),engraving=deck.battleCardInfoList.Sum(x=>x.cardEquipInfo.Sum(e=>e.empowerEffect.Sum(v=>(double)v))),growth=deck.battleCardInfoList.Sum(x=>x.effectDataInfoList.Sum(e=>(double)e.effectDataValue))});
                    if(offer.Rank<=100)Check(deck.battleCardInfoList.All(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.cardId).Rank==Math.Min((int)runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_RANK],runtime.Catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE))&&x.cardLevel==ProgressionRules.CardMax(runtime.Catalog,new LocalPlayer{Level=offer.Level},runtime.Catalog.Cards.Single(c=>c.Id==x.cardId))),"Top 100 opponents use actual native max-star cards at their original level cap: "+string.Join(',',deck.battleCardInfoList.Select(x=>x.cardId+"@"+x.cardLevel)));
                }
                var before=JsonSerializer.Serialize(p.Competitive.Offers[12]);p.Level=40;p.Competitive.Records[12].LossStreak=6;
                await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
                Check(JsonSerializer.Serialize(p.Competitive.Offers[12])==before,"Ladder previews cannot reroll after viewer level/loss changes");
            }
            p.Level=40;p.Competitive.Records[12]=new(){Battles=150,Wins=80,Rank=30,LossStreak=6};p.Competitive.Offers.Remove(12);
            await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            Check(p.Competitive.Offers[12].All(x=>x.Grade==4&&x.Level==99),"High placement cannot be reduced to low-level starter bots by loss protection");
            // Migrate legacy lobbies, but never mutate an admitted enemy snapshot.
            p.Level=99;foreach(var offer in p.Competitive.Offers[12]){offer.LadderVersion=0;offer.Grade=0;offer.GrowthStage=0;}
            var legacy=p.Competitive.Offers[12][0];var active=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=legacy.Uid});
            byte[] captured=p.Competitive.Enemy.ToArray();var legacyOffers=JsonSerializer.Serialize(p.Competitive.Offers[12]);
            await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            Check(captured.SequenceEqual(p.Competitive.Enemy)&&legacyOffers==JsonSerializer.Serialize(p.Competitive.Offers[12]),"An admitted legacy battle survives ladder migration unchanged");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=12,stamp=active.stamp});
            await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            Check(p.Competitive.Offers[12].All(x=>x.LadderVersion==1&&x.Grade==4),"Next idle lobby upgrades old weak offers without touching a battle");
            var upgraded=p.Competitive.Offers[12][0];
            var upgradedPreview=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=upgraded.Uid});
            var upgradedStart=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=upgraded.Uid});
            Check(JsonSerializer.Serialize(upgradedPreview.battleCardInfoList)==JsonSerializer.Serialize(upgradedStart.enemyCards),"Elite preview and admitted battle preserve identical cards, engraving, growth and skills");
            var upgradedSnapshot=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),p.Competitive.Enemy)!;
            Check(upgradedSnapshot.cardInfoList.SelectMany(x=>x.empowerEffectSum).Any(x=>x>0)&&upgradedSnapshot.cardInfoList.Zip(upgradedStart.enemyCards).All(pair=>pair.First.empowerEffectSum.Select((value,i)=>value==pair.Second.cardEquipInfo.Sum(e=>(long)e.empowerEffect.ElementAtOrDefault(i))).All(x=>x)),"Elite replay snapshot contains the same real engraving as native battle cards");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=12,stamp=upgradedStart.stamp});
            p.Level=99;
            var infinite=await peer.Request<GsSearchInfiniteArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_INFINITE_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchInfiniteArenaBattleEnemyListRequest());
            foreach(uint badge in new[]{2u,1u,0u}){
                var target=infinite.enemyList.First(x=>x.enemyRankGrade==badge);var spec=p.Competitive.Offers[31].Single(x=>x.Uid==target.enemyUid);
                var teams=await peer.Request<GsGetInfiniteArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_INFINITE_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetInfiniteArenaBattleEnemyDeckInfoRequest{targetUid=target.enemyUid});
                Check(teams.battleCardInfoList.Count==15&&teams.formationInfoList.Count==3,"Infinite category "+badge+" contains three complete native teams");
                int stars=new[]{3,5,6,8,12}[spec.Grade];
                Check(teams.battleCardInfoList.All(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.cardId).Rank==stars),"Infinite category "+badge+" keeps all fifteen enemy cards at the selected star tier");
            }
            p.Competitive.Records[31]=new(){Battles=150,Wins=80,Rank=100};p.Competitive.Offers.Remove(31);
            var eliteList=await peer.Request<GsSearchInfiniteArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_INFINITE_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchInfiniteArenaBattleEnemyListRequest());
            foreach(var target in eliteList.enemyList.Take(3)){
                var teams=await peer.Request<GsGetInfiniteArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_INFINITE_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetInfiniteArenaBattleEnemyDeckInfoRequest{targetUid=target.enemyUid});
                Check(teams.battleCardInfoList.Count==15&&teams.battleCardInfoList.All(x=>runtime.Catalog.Cards.Single(c=>c.Id==x.cardId).Rank==runtime.Catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE)),"Front-rank infinite arena keeps all three teams at the selected max-star tier");
                Check(teams.battleCardInfoList.All(x=>x.cardEquipInfo.Any(e=>e.empowerEffect.Any(v=>v>0))),"All three elite infinite teams retain real engraving after serial remapping");
                persistedEliteUid=target.enemyUid;persistedElite=WireCodec.Serialize(teams);
            }
            p.Competitive.Records[12]=new();p.Competitive.Offers.Remove(12);
            var fresh=await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            var originalOffers=p.Competitive.Offers[12].Select(offer=>offer.Uid).ToHashSet();
            persistedUid=fresh.enemyList.First(x=>x.enemyRankGrade==2).enemyUid;
            var preview=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=persistedUid});persistedPreview=WireCodec.Serialize(preview);
            p.Level=40;var repeated=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=persistedUid});
            Check(persistedPreview.SequenceEqual(WireCodec.Serialize(repeated)),"A level change after preview cannot silently regenerate the selected enemy");p.Level=99;
            var match=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=persistedUid});
            Check(match.enemyCards.Select(x=>x.cardId).SequenceEqual(preview.battleCardInfoList.Select(x=>x.cardId))&&match.enemyCards.Select(x=>x.cardLevel).SequenceEqual(preview.battleCardInfoList.Select(x=>x.cardLevel)),"Explicit start uses the exact previewed roster and level");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=12,stamp=match.stamp});
            Check(p.Competitive.Records[12].Battles==0&&p.Competitive.Records[12].LossStreak==0,"Cancelling a match does not fabricate a loss or alter difficulty progression");
            var automatic=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=10});
            Check(p.Competitive.Offers[10].Single(x=>x.Uid==automatic.enemy.enemyUid).Grade==0,"Initial automatic ranked matchmaking selects an introductory opponent");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=10,stamp=automatic.stamp});
            var started=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=12,targetUid=persistedUid});
            var pending=p.Competitive;var capturedEnemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),pending.Enemy)!;
            var replay=new PKBattleReplayData{gameMode=(BscTable.GAME_MODE)12,winTeamIndex=1};replay.thumbNailInfo.Add(new(){uid=p.Uid});replay.thumbNailInfo.Add(new(){uid=pending.Opponent});
            uint slot=0;foreach(var seq in pending.Cards)replay.battleCardInfo.Add(new(){teamIndex=0,cardId=p.CardIds[(int)seq-1],cardSlotIndex=slot++,uid=p.Uid});
            slot=0;foreach(var card in capturedEnemy.cardInfoList)replay.battleCardInfo.Add(new(){teamIndex=1,cardId=card.cardId,cardSlotIndex=slot++,uid=pending.Opponent});replay.battleProcessInfo.Add(new());
            byte[] compressed;using(var output=new MemoryStream()){using(var z=new ZLibStream(output,CompressionLevel.Fastest,true))z.Write(WireCodec.Serialize(replay));compressed=output.ToArray();}
            await peer.Request<GsUploadReplayDataOk>(GameServerCommand.GS_UPLOAD_REPLAY_DATA_REQUEST,new GsUploadReplayDataRequest{gameMode=(BscTable.GAME_MODE)12,totalReplayDataSize=(uint)compressed.Length,currentReplayDataSize=(uint)compressed.Length,replayData=compressed,firstSendDataFlag=true});
            var end=new GsUpdateArenaBattleResultRequest{targetUid=persistedUid,battleResult=1,battleEndSyncData=new(),battleHistory=new()};end.useCardSeq.AddRange(pending.Cards);
            await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end);
            Check(p.Competitive.Records[12].LossStreak==1&&!p.Competitive.Offers.ContainsKey(12),"Only a replay-validated result updates loss streak and expires next-match offers");
            await peer.Request<GsUpdateArenaBattleResultOk>(GameServerCommand.GS_UPDATE_ARENA_BATTLE_RESULT_REQUEST,end,retry:true);
            Check(p.Competitive.Records[12].LossStreak==1,"A settlement retry cannot count the same loss twice");
            await peer.Request<GsSearchArenaBattleEnemyListOk>(GameServerCommand.GS_SEARCH_ARENA_BATTLE_ENEMY_LIST_REQUEST,new GsSearchArenaBattleEnemyListRequest());
            var refreshed=p.Competitive.Offers[12];
            Check(refreshed.Any(offer=>!originalOffers.Contains(offer.Uid))&&refreshed.Any(offer=>originalOffers.Contains(offer.Uid)),
                "Validated match rotates some residents without discarding the whole bracket");
            persistedUid=refreshed.First(offer=>offer.Badge==2).Uid;
            persistedPreview=WireCodec.Serialize(await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=persistedUid}));
            p.Competitive.Records[10]=new(){Rank=1,Battles=150,Wins=100};
            p.Competitive.Offers.Remove(10);
            var topStart=await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=1,mode=10});
            var top=p.Competitive.Offers[10];
            Check(top.All(offer=>offer.Rank>1)&&top.Select(offer=>offer.Rank).Distinct().Count()==top.Count,
                "First place is never offered the player's own displayed rank");
            await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=3,mode=10,stamp=topStart.stamp});
            Check(JsonSerializer.Serialize(runtime.Router.Bots.All())==residents,"Tiered competition leaves every resident's saved profile, equipment and identity untouched");runtime.Store.Save();
        }
        await using(var restart=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=restart.Store.All().Single();using var peer=new Peer(root);await peer.Login(restart,account);
            var preview=await peer.Request<GsGetArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetArenaBattleEnemyDeckInfoRequest{targetUid=persistedUid});
            Check(WireCodec.Serialize(preview).SequenceEqual(persistedPreview),"Restart restores the exact offered opponent instead of rerolling its equipment");
            Check(p.Competitive.Records[12].LossStreak==1,"Loss protection progression persists across restart");
            var elite=await peer.Request<GsGetInfiniteArenaBattleEnemyDeckInfoOk>(GameServerCommand.GS_GET_INFINITE_ARENA_BATTLE_ENEMY_DECK_INFO_REQUEST,new GsGetInfiniteArenaBattleEnemyDeckInfoRequest{targetUid=persistedEliteUid});
            Check(WireCodec.Serialize(elite).SequenceEqual(persistedElite),"Restart restores all fifteen elite cards with identical engraving, growth and skills");
        }
        var report=new{passed=true,checks,samples,folder,normalSaveUsed=false,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()};
        File.WriteAllText("I:/BnsmModernWork/Verification/pvp-difficulty-server-latest.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PVP_DIFFICULTY_PASSED "+checks.Count);
    }
}
