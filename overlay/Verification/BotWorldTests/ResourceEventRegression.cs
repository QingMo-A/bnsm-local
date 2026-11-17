using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ResourceEventRegression
{
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","resource-events-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string attendanceFile=Path.Combine(root,"LocalServer/data/attendance.json"),gachaFile=Path.Combine(root,"LocalServer/data/gacha.json");
        string attendanceHash=Hash(attendanceFile),gachaHash=Hash(gachaFile);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();p.Stamina=10000;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var rules=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",flags)!.GetValue(runtime.Router)!;
        var catalog=(BoardResourceCatalog)typeof(ProtocolRouter).GetProperty("BoardResources",flags)!.GetValue(runtime.Router)!;
        var spec=rules.Operations.First(x=>x.Type=="RESOURCE_STAGE_ALL_OPEN");
        string originalStart=spec.Start,originalEnd=spec.End,originalType=spec.Type;
        ulong At(int day,int hour,int minute=0,int second=0)=>(ulong)new DateTimeOffset(2026,11,day,hour,minute,second,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        spec.Start="2026-11-01 23:00:00";spec.End="2026-11-02 01:00:00";
        Check(!rules.ResourceAllOpen(At(1,22,59,59))&&rules.ResourceAllOpen(At(1,23))&&rules.ResourceAllOpen(At(2,1))&&!rules.ResourceAllOpen(At(2,1,0,1)),"Activity starts and ends inclusively in UTC+8, including midnight");
        spec.Type="EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN";
        Check(rules.ResourceAllOpen(At(2,0)),"Full and short operation names have the same behavior");
        spec.Start="invalid";Check(!rules.ResourceAllOpen(At(2,0)),"Malformed activity dates cannot unlock closed stages");
        spec.Type=originalType;spec.Start=originalStart;spec.End=originalEnd;
        const ulong reportedTime=1793550154; // 2026-11-01 16:22:34 UTC from the supplied log.
        Check(!rules.ResourceAllOpen(reportedTime),"Legacy permanent all-open seed no longer overrides weekly opening");
        spec.Start="2026-11-01 00:00:00";spec.End="2026-11-07 23:59:59";
        Check(rules.ResourceAllOpen(reportedTime),"Explicit limited all-open event remains supported");
        var reportedClosed=catalog.Resources.Where(x=>!ProtocolRouter.ResourceSchedule(x,reportedTime)).ToArray();
        Check(reportedClosed.Length>0&&reportedClosed.All(x=>runtime.Router.ResourceOpen(x,reportedTime)),"Reproduced closed-weekday stages are admitted by the same activity sent to the client");
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        GsResourceStageBattleStartRequest Start(BscTable.ResourceStageLink row)=>new(){stageid=(uint)row.Id,difficulty=0,deckid=(uint)row.UseDeck};
        void Deck(BscTable.ResourceStageLink row){p.Decks[(uint)row.UseDeck]=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).Where(x=>row.EnterType==BscTable.CARD_FACTION.FACTION_ALL||runtime.Catalog.Cards.Single(c=>c.Id==x.id).Faction==row.EnterType).Take(5).Select(x=>x.seq).ToList();}
        string State()=>JsonSerializer.Serialize(new{p.Stamina,p.Gold,p.CashPoint,p.Resources,p.ActiveStage,p.ActiveBattleStamp});
        async Task Bad(GameServerCommand command,object request,ServerErrorCode code,string label){string before=State();var fail=await peer.Request<FailPacket>(command,request,success:false);Check(fail.errorCode==(uint)code&&State()==before,label);}
        async Task Finish(BscTable.ResourceStageLink row,GsResourceStageBattleStartOk started,bool won)=>
            await peer.Request<GsResourceStageBattleEndConfirmOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_END_CONFIRM_REQUEST,new GsResourceStageBattleEndConfirmRequest{stageId=(uint)row.Id,difficulty=0,timeStamp=started.timeStamp,endType=won?NormalBattleEndType.NORMAL_BATTLE_SUCCESS:NormalBattleEndType.NORMAL_BATTLE_FAIL,enemyAliveCount=won?unchecked((uint)((started.timeStamp%1000)*(uint)row.Id)):0,battleEndSyncData=new()});
        var usage=await peer.Request<GsResourceStageGetUseCountOk>(GameServerCommand.GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST,new GsResourceStageGetUseCountRequest());
        Check(usage.isInAllOpenEvent&&usage.useCount.Count==8,"Entrance response advertises the same effective all-open event for all eight stages");
        foreach(var row in catalog.Resources){
            Deck(row);uint before=p.Stamina;var request=Start(row);
            var result=await peer.Request<GsResourceStageBattleStartOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,request);
            Check(result.waveProbIdList.Count>0&&p.Stamina==before-row.ConsumeStamina&&p.Resources.Used[(uint)row.Id]==1,"Native resource battle starts and charges exactly once: "+row.Id);
            string startedState=State();await peer.Request<GsResourceStageBattleStartOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,request,retry:true);
            Check(State()==startedState,"Rapid duplicate start does not charge twice: "+row.Id);
            await Finish(row,result,false);
        }
        var closed=catalog.Resources.First(x=>!ProtocolRouter.ResourceSchedule(x,ProtocolRouter.Now));Deck(closed);
        var start=await peer.Request<GsResourceStageBattleStartOk>(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,Start(closed));
        // Activity ending after the start must not invalidate a legitimate battle result.
        spec.End="2020-01-01 00:00:00";await Finish(closed,start,true);
        Check(p.Resources.Cleared.Contains((uint)closed.Id*10)&&p.ActiveStage==null,"A battle accepted during the event can settle after the event ends");
        usage=await peer.Request<GsResourceStageGetUseCountOk>(GameServerCommand.GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST,new GsResourceStageGetUseCountRequest());
        Check(!usage.isInAllOpenEvent,"Expired activity stops advertising all-open at the entrance");
        await Bad(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,Start(closed),ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN,"Closed start gives the existing localized not-open error and charges nothing");
        var sweep=new GsResourceStageSkipRequest{stageId=(uint)closed.Id,difficulty=0,skipCount=1};
        await Bad(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,sweep,ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN,"Closed sweep uses the same opening rule without spending stamina");
        var reset=new GsMoneyTypeResourceChargeRequest{moneyTypeEvent=BscTable.MONEY_TYPE_EVENT.MONEY_EVENT_RESOURCE_STAGE_RESET_COST,param1=closed.Id};
        await Bad(GameServerCommand.GS_MONEY_TYPE_RESOURCE_CHARGE_REQUEST,reset,ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN,"Closed reset uses the same opening rule without spending diamonds");
        Check(catalog.Resources.Where(x=>ProtocolRouter.ResourceSchedule(x,ProtocolRouter.Now)).All(x=>runtime.Router.ResourceOpen(x,ProtocolRouter.Now)),"Ordinary scheduled stages remain open when the activity is expired");
        spec.Start="2099-01-01 00:00:00";spec.End="2099-12-31 23:59:59";
        Check(!runtime.Router.ResourceOpen(closed,ProtocolRouter.Now),"A future all-open activity cannot unlock closed stages early");
        spec.Start="2026-11-01 00:00:00";spec.End="2026-11-07 23:59:59";
        uint stamina=p.Stamina;var swept=await peer.Request<GsResourceStageSkipOk>(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,sweep);
        Check(swept.earnedList.Count==1&&p.Stamina==stamina-closed.ConsumeStamina,"All-open also permits a previously cleared off-schedule stage to sweep");
        p.Resources.Used[(uint)closed.Id]=(uint)closed.EnterCount;
        await Bad(GameServerCommand.GS_MONEY_TYPE_RESOURCE_CHARGE_REQUEST,reset,ServerErrorCode.ERROR_INVALID_ID,"All-open preserves the catalog's zero VIP reset allowance");
        // Isolated in-memory quota fixture verifies the reset schedule independently.
        runtime.Catalog.VipFor(p.VipExp).ResourceMaxResetCount=1;
        uint diamonds=p.CashPoint;await peer.Request<GsMoneyTypeResourceChargeOk>(GameServerCommand.GS_MONEY_TYPE_RESOURCE_CHARGE_REQUEST,reset);
        Check(p.Resources.Used[(uint)closed.Id]==0&&p.Resources.Resets[(uint)closed.Id]==1&&p.CashPoint<diamonds,"All-open permits an off-schedule daily reset under the normal VIP and price rules");
        var invalid=Start(closed);invalid.stageid=uint.MaxValue;
        await Bad(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,invalid,ServerErrorCode.ERROR_INVALID_ID,"All-open does not bypass stage identity validation");
        invalid=Start(closed);invalid.difficulty=(BscTable.RESOURCE_DIFFICULTY)6;
        await Bad(GameServerCommand.GS_RESOURCE_STAGE_BATTLE_START_REQUEST,invalid,ServerErrorCode.ERROR_INVALID_ID,"All-open does not bypass difficulty validation");
        Check(attendanceHash==Hash(attendanceFile)&&gachaHash==Hash(gachaFile),"Production activities and immutable catalog bytes were not modified");
        File.WriteAllText("I:/BnsmModernWork/Verification/resource-event-tests.json",JsonSerializer.Serialize(new{passed=true,checks,fixture=folder,productionSaveUsed=false,reportedFailureUtc="2026-11-01T16:22:34Z",reportedClosedStageIds=reportedClosed.Select(x=>x.Id)},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("RESOURCE_EVENT_TESTS_PASSED "+checks.Count);
    }
}
