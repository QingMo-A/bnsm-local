using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class BattleLifecycleRegression
{
    internal static void Prepare(LocalRuntime runtime)
    {
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        p.Stamina=10000;p.ClearHardStage=runtime.Catalog.HardStages.Keys.Max();p.ClearStage=runtime.Catalog.Stages.Keys.Max();
        p.Decks[21]=p.Deck.ToList();p.Decks[22]=p.Deck.ToList();p.Decks[1]=p.Deck.ToList();
        p.AttendanceLastDay=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;
        p.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now),Difficulty=1,Spot=1};
        runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);Prepare(runtime);
        var p=runtime.Store.All().Single();var bot=new LocalBotBuilder(runtime.Catalog).Build(999111,"Lifecycle fixture","legend","balanced",42,ProtocolRouter.Now).Profile;
        p.CardIds=bot.CardIds;p.CardStates=bot.CardStates;p.EquipmentItems=bot.EquipmentItems;p.NextEquipmentSequence=bot.NextEquipmentSequence;p.Inventory=bot.Inventory;
        p.Deck=bot.Deck;p.Decks[21]=p.Deck.ToList();p.Decks[22]=p.Deck.ToList();p.Decks[1]=p.Deck.ToList();
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;var attendance=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",flags)!.GetValue(runtime.Router)!;
        foreach(var operation in attendance.Operations.Where(x=>x.Type.Contains("RESOURCE_STAGE_ALL_OPEN")))operation.End="2000-01-01 00:00:00";
        runtime.Store.Save();
        await using var app=await DebugToolsTest.Host(runtime);
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,gamePort=runtime.GamePort}));
        await Task.Delay(TimeSpan.FromMinutes(20));
    }
    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/battle-lifecycle-"+Guid.NewGuid().ToString("N");
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        ulong last=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            Prepare(runtime);var p=runtime.Store.All().Single();using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            runtime.Catalog.Numbers[BscTable.NUMBER_TYPE.BIG_BOSS_BATTLE_COOLTIME_SEC]=60; // Isolated boundary scenario, no catalog file edits.
            async Task<FailPacket> Bad(GameServerCommand cmd,object request,ServerErrorCode expected,string text) {
                var packet=await peer.Request<FailPacket>(cmd,request,success:false);
                Check(packet.errorCode==(uint)expected&&packet.message.StartsWith("BNSM/1\n")&&packet.message.Contains("问题编号："),text);return packet;
            }
            foreach(uint boss in new uint[]{2635101,2637101}) {
                p.Raids.Used[4]=0;p.Raids.Latest[4]=0;
                var first=await peer.Request<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=boss});
                uint used=p.Raids.Used[4];ulong latest=p.Raids.Latest[4];string hp=JsonSerializer.Serialize(p.Raids.BossHp);
                await Bad(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{stageid=13,deckid=1},ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS,"Active extreme boss blocks overlapping main battle with an actual battle-state error: "+boss);
                var retreat=new LocalRankCombatRequest{operation=10,mode=33,stamp=first.timeStamp};
                await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat);
                Check(p.ActiveStage==null&&p.ActiveBattleStamp==0&&p.Raids.ActiveMode==0&&p.Raids.BattleCards.Count==0,"Explicit retreat clears boss/global markers: "+boss);
                Check(p.Raids.Used[4]==used&&p.Raids.Latest[4]==latest&&JsonSerializer.Serialize(p.Raids.BossHp)==hp,"Retreat preserves admission/cooldown/prior boss HP, without fabricated damage: "+boss);
                await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat,retry:true);
                Check(p.Raids.Used[4]==used,"Repeated retreat is idempotent: "+boss);
                await Bad(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=boss},ServerErrorCode.ERROR_BIG_BOSS_BATTLE_COOLTIME,"Immediate reentry reports cooldown specifically: "+boss);
                var normal=await peer.Request<GsNormalBattleStartOk>(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{stageid=13,deckid=1});
                Check(normal.timeStamp>first.timeStamp,"Main battle starts immediately after boss retreat with a fresh monotonic token: "+boss);
                await peer.Request<GsNormalBattleEndConfirmOk>(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,new GsNormalBattleEndConfirmRequest{stageId=13,timeStamp=normal.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_FAIL,battleEndSyncData=new()});
                p.Raids.Latest[4]=0; // Isolated clock-expiry fixture only.
                var second=await peer.Request<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=boss});
                Check(second.timeStamp>normal.timeStamp,"Extreme boss reenters after its legitimate cooldown: "+boss);
                await Bad(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,new LocalRankCombatRequest{operation=10,mode=33,stamp=first.timeStamp},ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS,"Late retreat cannot erase a newer battle: "+boss);
                Check(p.ActiveBattleStamp==second.timeStamp,"Newer token remains active after rejected old retreat: "+boss);
                uint gold=p.Gold;
                await Bad(GameServerCommand.GS_BIG_BOSS_BATTLE_END_REQUEST,new GsBigBossBattleEndRequest{bossID=boss,timeStamp=first.timeStamp,damageAmount=100},ServerErrorCode.ERROR_INVALID_BIG_BOSS_BATTLE_STATUS,"Late abandoned boss result cannot award a reward: "+boss);
                Check(p.Gold==gold&&p.ActiveBattleStamp==second.timeStamp,"Rejected late result preserves all new battle state: "+boss);
                await peer.Request<GsGetBigBossListOk>(GameServerCommand.GS_GET_BIG_BOSS_LIST_REQUEST,new GsGetBigBossListRequest());
                Check(p.ActiveStage==null,"Legacy boss lobby also repairs an unfinished attempt: "+boss);
            }
            p.Raids.Used[4]=0;p.Raids.Latest[4]=0;p.Raids.BossHp[2635101]=0;
            await Bad(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=2635101},ServerErrorCode.ERROR_BOSS_IS_ALREADY_CLEARED,"Defeated boss has a distinct cleared error, not INVALID_ID");
            // The reserve becomes frontline during native setup, so it may be
            // absent from the final sideDeckCardSeq list entirely.
            p.Decks[22]=[1,0,0,0,0];
            var begin=new LocalWanderStartRequest{spotId=1,hasRoster=true,cards=[1,0,0,0,0],reserves=[6,7,0]};
            var start=await peer.Request<GsWanderBattleStartOk>(GameServerCommand.GS_WANDER_BATTLE_START_REQUEST,begin);
            Check(p.Raids.BattleSideCards.SequenceEqual(new ulong[]{6,7}),"Entry binds the original client-local reserve roster and ignores empty slots");
            var result=new GsWanderBattleEndConfirmRequest{spotId=1,startTimeStamp=start.startTimeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(start.startTimeStamp%1000),battleEndSyncData=new()};
            result.cardInfoList.AddRange(new[]{new PKWanderBattleUsedCardInfo{cardSeq=1,hpRatio=.5f,spRatio=20},new PKWanderBattleUsedCardInfo{cardSeq=6,hpRatio=.8f,spRatio=30},new PKWanderBattleUsedCardInfo{cardSeq=7,hpRatio=.7f,spRatio=10}});
            result.battleEndSyncData.usedSideDeckCardSeq.Add(6);result.battleEndSyncData.sideDeckCardSeq.AddRange(new ulong[]{7,0});
            var outsider=new PKWanderBattleUsedCardInfo{cardSeq=8,hpRatio=1};result.cardInfoList.Add(outsider);uint beforeGold=p.Gold;
            await Bad(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,result,ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY,"Unselected owned card cannot be smuggled into survivor settlement");
            Check(p.ActiveBattleStamp==start.startTimeStamp&&p.Gold==beforeGold&&p.Wander.Spot==1,"Malformed result leaves the valid attempt available for retry");
            result.cardInfoList.Remove(outsider);
            var settled=await peer.Request<GsWanderBattleEndConfirmOk>(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,result);
            Check(p.Wander.Spot==2&&p.Wander.Points>0&&p.ActiveStage==null&&p.Wander.Cards[6].Hp==.8f,"Promoted reserves settle with real HP/SP and advance the journey");
            uint goldNow=p.Gold,points=p.Wander.Points;await peer.Request<GsWanderBattleEndConfirmOk>(GameServerCommand.GS_WANDER_BATTLE_END_CONFIRM_REQUEST,result,retry:true);
            Check(p.Gold==goldNow&&p.Wander.Points==points,"Repeated wander result cannot duplicate rewards or progress");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var attendance=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",flags)!.GetValue(runtime.Router)!;
            foreach(var operation in attendance.Operations.Where(x=>x.Type.Contains("RESOURCE_STAGE_ALL_OPEN")))operation.End="2000-01-01 00:00:00";
            var board=new BoardResourceCatalog(root);var usage=await peer.Request<LocalResourceAvailabilityReply>(GameServerCommand.GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST,new GsResourceStageGetUseCountRequest());
            Check(usage.serverTime>0&&!usage.isInAllOpenEvent,"Resource UI receives confirmed server time and effective event flag");
            foreach(var row in board.Resources)for(int day=1;day<=7;day++)foreach(int hour in new[]{0,3,4,12,23}) {
                ulong stamp=(ulong)new DateTimeOffset(2026,11,day,hour,0,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
                Check(ProtocolRouter.ResourceSchedule(row,stamp)==LocalResourceSchedule.IsOpen(row,stamp),"Shared resource rule: "+row.Id+" day="+day+" hour="+hour);
            }
            var closed=board.Resources.First(x=>!ProtocolRouter.ResourceSchedule(x,ProtocolRouter.Now));
            await Bad(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,new GsResourceStageBattleStartRequest{stageid=(uint)closed.Id,deckid=(uint)closed.UseDeck},ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN,"Off-schedule stage is blocked by server with an explicit not-open reason");
            var monday=new BscTable.ResourceStageLink{Mon=true,StartTime="00:00:00",EndTime="23:59:59"};
            ulong monday0030=(ulong)new DateTimeOffset(2026,11,2,0,30,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
            Check(LocalResourceSchedule.IsOpen(monday,monday0030),"Monday before 04:00 uses Monday, independent of quota reset time");
            monday.StartTime="22:00:00";monday.EndTime="02:00:00";
            Check(LocalResourceSchedule.IsOpen(monday,monday0030+86400)&&!LocalResourceSchedule.IsOpen(monday,monday0030),"Overnight windows use the opening day's weekday");
            p.Wander=new(){Day=ProtocolRouter.TowerDay(ProtocolRouter.Now),Difficulty=1,Spot=1};p.Raids.Used[4]=0;p.Raids.Latest[4]=0;p.Raids.BossHp.Clear();
            var abandoned=await peer.Request<GsBigBossBattleStartOk>(GameServerCommand.GS_BIG_BOSS_BATTLE_START_REQUEST,new GsBigBossBattleStartRequest{bossID=2637101});last=abandoned.timeStamp;runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(runtime,"local-player");var p=runtime.Store.All().Single();
            Check(p.ActiveStage==null&&p.Raids.ActiveStamp==0&&p.Raids.BattleSideCards.Count==0&&p.LastBattleStamp>=last,"Restart repairs orphaned raid and reserve markers without resetting the save");
            Check(p.Raids.Used[4]==1&&p.Raids.Latest[4]>0,"Restart preserves the legitimate consumed entry and cooldown");
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/battle-lifecycle-server-latest.json",JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,fixture=folder,productionSaveUsed=false,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("BATTLE_LIFECYCLE_PASSED "+checks.Count);
    }
}
