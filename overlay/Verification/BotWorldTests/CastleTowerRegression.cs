using System.Reflection;
using System.Text.Json;

internal static class CastleTowerRegression
{
    private static HashSet<int> Bans(LocalRuntime r,uint floor)=>(HashSet<int>)typeof(ProtocolRouter).GetMethod("TowerBan",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(r.Router,[floor])!;
    private static (ulong Banned,List<ulong> Allowed) Prepare(LocalRuntime r)
    {
        CompetitiveRegression.Prepare(r);var p=r.Store.All().Single();var bans=Bans(r,1);
        var row=r.Catalog.UsableHeroes.Where(x=>bans.Contains(x.EvolveFirstAncestor)).OrderByDescending(x=>x.Rank).First();
        p.CardIds.Add((uint)row.Id);ulong seq=(ulong)p.CardIds.Count;p.CardStates[seq]=new(){Level=99,Exp=ProgressionRules.CardFloor(r.Catalog,99),Enchant=5,ActiveSkills=[99,99],PassiveSkills=[99,99,99,99,99]};
        var allowed=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).Where(x=>!bans.Contains(r.Catalog.Cards.Single(c=>c.Id==x.id).EvolveFirstAncestor)).Take(5).Select(x=>x.seq).ToList();p.Decks[25]=allowed;
        p.Competitive.HomeMap=1;
        var castles=new CompetitiveCatalog("I:/BnsmModernWork/WindowsPreview");
        foreach(var spot in castles.CastleMaps.Single(x=>x.MapId==1).SpotList.Where(s=>castles.Castles.Single(c=>c.Id==s.CastleId).BossId==0))p.Competitive.Castles["1/"+spot.SpotId]=new(){Map=1,Spot=(uint)spot.SpotId,Status=1,WealthStamp=ProtocolRouter.Now};
        r.Store.Save();return(seq,allowed);
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");Directory.CreateDirectory(folder);
        await using var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);var setup=Prepare(r);
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,bannedSeq=setup.Banned,allowed=setup.Allowed,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new InvalidDataException(name);checks.Add(name);}
        string folder=Path.Combine("I:/BnsmModernWork/Verification","castle-tower-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        await using(var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var setup=Prepare(r);var p=r.Store.All().Single();using var peer=new Peer(root);await peer.Login(r,p.Account);
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,new GsChallengeStageLobbyRequest());
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Stamina,p.ChallengeTower,p.TowerReceipts,p.ActiveStage,p.ActiveBattleStamp});
            GsChallengeStageBattleStartRequest Start(uint floor=1,uint area=1)=>new(){difficulty=floor,stageid=area};
            async Task Bad(object req,ServerErrorCode code,string phrase,string label){string before=State();var f=await peer.Request<FailPacket>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,req,success:false);Check(f.errorCode==(uint)code&&f.message.Contains(phrase)&&!f.message.Contains("INVALID_ID"),label+" returns specific Chinese feedback");Check(State()==before,label+" preserves battle, costs, points and receipts");}
            await Bad(Start(999),ServerErrorCode.ERROR_INVALID_STAGE_ID,"关卡信息无效","Unknown stage");
            await Bad(Start(2),ServerErrorCode.ERROR_NO_OPEN_FLOOR,"尚未解锁","Locked floor");
            await Bad(Start(1,2),ServerErrorCode.ERROR_NOT_ENOUGH_STEP_CONDITION,"按顺序","Uncleared predecessor");
            p.Decks[25]=[setup.Banned];await Bad(Start(),ServerErrorCode.ERROR_USED_BAN_CARD,"禁用","Banned ancestor");p.Decks[25]=setup.Allowed;
            p.TrainingSlots[0]=new(){CardSequence=setup.Allowed[0],EndTime=ProtocolRouter.Now+60};await Bad(Start(),ServerErrorCode.ERROR_CARD_IN_TRAINING,"修炼","Training card");p.TrainingSlots.Clear();
            p.ChallengeTower.DeadCards.Add(setup.Allowed[0]);await Bad(Start(),ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO,"挑战塔已阵亡","Fallen card");p.ChallengeTower.DeadCards.Clear();
            p.Decks[25]=[ulong.MaxValue];await Bad(Start(),ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE,"出战卡组无效","Missing card");p.Decks[25]=setup.Allowed;
            var start=await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start());Check(start.timeStamp!=0&&p.ChallengeTower.BattleCards.SequenceEqual(setup.Allowed),"Valid replacement deck starts after rejected banned deck without relogin");
            await Bad(Start(),ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS,"上一场战斗","Active battle");
            var end=new GsChallengeStageBattleEndConfirmRequest{difficulty=1,stageid=1,timeStamp=start.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,enemyAliveCount=(uint)(start.timeStamp%1000),battleEndSyncData=new()};end.battleEndSyncData.sideDeckCardSeq.Add(setup.Banned);
            string before=State();var bad=await peer.Request<FailPacket>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,end,success:false);Check(bad.errorCode==(uint)ServerErrorCode.ERROR_USED_BAN_CARD&&bad.message.Contains("禁用")&&State()==before,"Banned reserve returns a specific failure without settling or altering active state");
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,new GsChallengeStageLobbyRequest());Check(p.ActiveStage==null&&p.ChallengeTower.UsedEntries==0,"Lobby recovers a rejected tower attempt without fabricated progress or cost");
            await peer.Request<GsEnterCastleMapOk>(GameServerCommand.GS_ENTER_CASTLE_MAP_REQUEST,new GsEnterCastleMapRequest());
            string map=JsonSerializer.Serialize(p.Competitive);
            foreach(var spot in p.Competitive.Castles.Values.ToArray()) {
                var detail=await peer.Request<GsGetCastleMapSpotDetailInfoOk>(GameServerCommand.GS_GET_CASTLE_MAP_SPOT_DETAIL_INFO_REQUEST,new GsGetCastleMapSpotDetailInfoRequest{mapId=spot.Map,spotId=spot.Spot,myCastleFlag=true});
                Check(detail.mapId==spot.Map&&detail.spotId==spot.Spot&&detail.castleBattleMapSpotDetailInfo.mapId==spot.Map&&detail.castleBattleMapSpotDetailInfo.spotId==spot.Spot,"Owned castle detail returns matching map/spot identity: "+spot.Spot);
            }
            Check(map==JsonSerializer.Serialize(p.Competitive),"Reading every owned castle detail cannot create an encounter or change ownership, quotas or battle state");
        }
        string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/castle-tower-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=Hash(typeof(LocalRuntime).Assembly.Location)},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("CASTLE_TOWER_TEST_OK "+checks.Count);
    }
}
