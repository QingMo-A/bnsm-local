extern alias game;
using System.Reflection;
using System.Text.Json;
using C = game::BscTable.CHECK_TYPE;

internal static class ResourceMissionRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","resource-missions-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();p.Stamina=10000;
            var table=new BoardResourceCatalog(root);using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            uint Count(C kind)=>(uint)p.Missions.DailyCounters.GetValueOrDefault(kind);
            var initial=await peer.Request<GsGetProgressingMissionListOk>(GameServerCommand.GS_GET_PROGRESSING_MISSION_LIST_REQUEST,new GsGetProgressingMissionListRequest());
            var missions=new MissionCatalog(root);
            uint task=initial.list.Single(x=>missions.Rows[x.missionId].CheckType==C.MCT_CLEAR_SOKSUNG_DUNGEON).missionId;
            Check(initial.list.Single(x=>x.missionId==task).count==0,"Native faction daily task starts incomplete");
            async Task<GsGetPromotionUserInfoOk> Activities()=>await peer.Request<GsGetPromotionUserInfoOk>(GameServerCommand.GS_GET_PROMOTION_USER_INFO_REQUEST,new GsGetPromotionUserInfoRequest());
            uint Activity(GsGetPromotionUserInfoOk info)=>info.promotionInfo.Single(x=>x.eventId==900003&&x.dataId==2).value;
            Check(Activity(await Activities())==0,"Challenge activity starts incomplete");
            void Deck(BscTable.ResourceStageLink row) {
                p.Decks[(uint)row.UseDeck]=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1))
                    .Where(x=>row.EnterType==BscTable.CARD_FACTION.FACTION_ALL||runtime.Catalog.Cards.Single(c=>c.Id==x.id).Faction==row.EnterType).Take(5).Select(x=>x.seq).ToList();
            }
            async Task<GsResourceStageBattleStartOk> Start(BscTable.ResourceStageLink row) {
                Deck(row);return await peer.Request<GsResourceStageBattleStartOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,new GsResourceStageBattleStartRequest{stageid=(uint)row.Id,difficulty=0,deckid=(uint)row.UseDeck});
            }
            GsResourceStageBattleEndConfirmRequest End(BscTable.ResourceStageLink row,GsResourceStageBattleStartOk started,bool won)=>new(){stageId=(uint)row.Id,difficulty=0,timeStamp=started.timeStamp,endType=won?NormalBattleEndType.NORMAL_BATTLE_SUCCESS:NormalBattleEndType.NORMAL_BATTLE_FAIL,enemyAliveCount=won?unchecked((uint)((started.timeStamp%1000)*(uint)row.Id)):0,battleEndSyncData=new()};
            string State()=>JsonSerializer.Serialize(new{p.Missions,p.Activities});
            uint factionWins=0,genericWins=0;
            foreach(var row in table.Resources.Where(x=>runtime.Router.ResourceOpen(x,ProtocolRouter.Now))) {
                bool faction=row.DungeonType==BscTable.RESOURCE_DUNGEON_TYPE.DUNGEON_SOKSUNG;
                uint before=Count(C.MCT_ENTER_SOKSUNG_DUNGEON);
                var started=await Start(row);
                Check(Count(C.MCT_ENTER_SOKSUNG_DUNGEON)==before+(faction?1u:0),"Entry classifies actual dungeon type: "+row.Id);
                Check(Count(C.MCT_CLEAR_SOKSUNG_DUNGEON)==factionWins,"Entering does not mark faction daily complete: "+row.Id);
                var end=End(row,started,true);
                await peer.Request<GsResourceStageBattleEndConfirmOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST,end);
                if(faction)factionWins++;genericWins++;
                Check(Count(C.MCT_CLEAR_SOKSUNG_DUNGEON)==factionWins&&Count(C.MCT_CLEAR_RESOURCE_DUNGEON)==genericWins,"Victory updates native daily and inclusive activity counters: "+row.Id);
                string state=State();await peer.Request<GsResourceStageBattleEndConfirmOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST,end,retry:true);
                Check(State()==state,"Retried settlement cannot duplicate progress: "+row.Id);
                await peer.Request<GsResourceStageSkipOk>(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,new GsResourceStageSkipRequest{stageId=(uint)row.Id,difficulty=0,skipCount=1});
                if(faction)factionWins++;genericWins++;
                Check(Count(C.MCT_CLEAR_SOKSUNG_DUNGEON)==factionWins&&Count(C.MCT_CLEAR_RESOURCE_DUNGEON)==genericWins,"Native sweep counts once in the matching mode: "+row.Id);
                started=await Start(row);
                await peer.Request<GsResourceStageBattleEndConfirmOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST,End(row,started,false));
                Check(Count(C.MCT_CLEAR_SOKSUNG_DUNGEON)==factionWins&&Count(C.MCT_CLEAR_RESOURCE_DUNGEON)==genericWins,"Defeat does not complete a clear objective: "+row.Id);
            }
            Check(factionWins>0,"At least one real faction stage was exercised");
            var changed=await peer.Request<GsGetChangedMissionListOk>(GameServerCommand.GS_GET_CHANGED_MISSION_LIST_REQUEST,new GsGetChangedMissionListRequest());
            Check(changed.list.Single(x=>x.missionId==task).status==MissionStatus.MISSION_STATUS_COMPLETED,"Changed-task protocol exposes completed faction daily row");
            var activity=await Activities();
            Check(Activity(activity)==1&&activity.promotionRewardInfo.Any(x=>x.eventId==900003&&x.dataId==2&&x.status==0),"Fresh activity protocol exposes the clear reward as claimable");
            await peer.Request<GsMissionRewardOk>(GameServerCommand.GS_MISSION_REWARD_REQUEST,new GsMissionRewardRequest{missionId=task});
            Check(p.Missions.DailyClaimed.ContainsKey(task),"Completed native faction task reward can be claimed");
            await peer.Request<GsReceivePromotionRewardOk>(GameServerCommand.GS_RECEIVE_PROMOTION_REWARD_REQUEST,new GsReceivePromotionRewardRequest{eventId=900003,dataId=2,promotionType=BscTable.PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION});
            Check(p.Activities.DailyClaims.Contains("900003/2"),"Challenge activity reward can be claimed independently");
            runtime.Store.Save();
        }
        await using(var reopened=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var p=reopened.Store.All().Single();
            Check(p.Missions.DailyCounters.GetValueOrDefault(C.MCT_CLEAR_SOKSUNG_DUNGEON)>0&&p.Activities.DailyClaims.Contains("900003/2"),"Restart preserves mode progress and claims");
        }
        string serverCoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/resource-missions-latest.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,productionSaveUsed=false,serverCoreSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("RESOURCE_MISSIONS_PASSED "+checks.Count);
    }
}
