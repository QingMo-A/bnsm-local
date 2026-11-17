using System.Text.Json;
using System.Security.Cryptography;

internal static class WorldRaidSettlementRegression
{
    internal static void Prepare(LocalRuntime r)
    {
        CompetitiveRegression.Prepare(r);var p=r.Store.All().Single();p.Decks[23]=p.Deck.ToList();p.Decks[24]=p.Deck.ToList();r.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");Directory.CreateDirectory(folder);
        await using var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);Prepare(r);
        // Exercise the preserved cooldown and native paid-reset path at a short
        // isolated boundary; shipped catalogs are left untouched.
        r.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLD_BOSS_BATLE_COOLTIME_SEC]=60;
        r.Catalog.Numbers[BscTable.NUMBER_TYPE.WORLD_BOSS_COMBO_BATLE_COOLTIME_SEC]=60;
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(20));
    }
    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/world-raid-server-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        await using(var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            Prepare(r);var p=r.Store.All().Single();using var peer=new Peer(root);await peer.Login(r,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.ActiveStage,p.ActiveBattleStamp,p.Raids,p.WorldBossPoint,p.Gold,p.CashPoint,p.Ranking});
            foreach(bool combo in new[]{false,true}){
                int mode=combo?3:2;var info=await peer.Request<GsGetWorldBossInfoOk>(GameServerCommand.GS_GET_WORLD_BOSS_INFO_REQUEST,new GsGetWorldBossInfoRequest());uint id=combo?info.comboBossId:info.worldBossId;
                var begin=new GsStartWorldBossBattleRequest{isCombo=combo,bossId=id};var start=await peer.Request<GsStartWorldBossBattleOk>(GameServerCommand.GS_START_WORLD_BOSS_BATTLE_REQUEST,begin);ulong stamp=(ulong)start.timeStamp;string started=State();
                for(int i=0;i<3;i++)await peer.Request<GsGetWorldBossInfoOk>(GameServerCommand.GS_GET_WORLD_BOSS_INFO_REQUEST,new GsGetWorldBossInfoRequest());
                Check(State()==started,"World info reads during loading/combat preserve active token, HP, entry and cooldown: "+mode);
                var wrong=new GsEndWorldBossBattleRequest{bossId=id,isCombo=!combo,timeStamp=start.timeStamp,damageAmount=1302000,battleEndSyncData=new(),battleHistory=new()};
                var fail=await peer.Request<FailPacket>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,wrong,success:false);Check(fail.errorCode==(uint)ServerErrorCode.ERROR_INVALID_BIG_BOSS_BATTLE_STATUS&&State()==started,"Wrong mode settlement cannot clear the current encounter or grant rewards: "+mode);
                var end=new GsEndWorldBossBattleRequest{bossId=id,isCombo=combo,timeStamp=start.timeStamp,damageAmount=1302000,battleEndSyncData=new(),battleHistory=new()};uint before=p.WorldBossPoint;
                var result=await peer.Request<GsEndWorldBossBattleOk>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,end);Check(result.earnedCoinInfo.worldBossPoint>0&&p.WorldBossPoint==before+result.earnedCoinInfo.worldBossPoint&&p.ActiveStage==null&&p.Raids.ActiveStamp==0,"World battle settles once after background list reads: "+mode);
                Check(p.Raids.BestDamage[id]==1302000&&(combo?p.Ranking.ComboBest:p.Ranking.WorldBest)==1302000,"Correct damage record and ranking category preserved: "+mode);
                string settled=State();await peer.Request<GsEndWorldBossBattleOk>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,end,retry:true);Check(State()==settled,"Lost settlement acknowledgement retry cannot duplicate essence or records: "+mode);
                uint used=p.Raids.Used[mode];ulong latest=p.Raids.Latest[mode];
                // An isolated clock-expiry setup permits another entry without changing catalog gates.
                p.Raids.Latest[mode]=0;var next=await peer.Request<GsStartWorldBossBattleOk>(GameServerCommand.GS_START_WORLD_BOSS_BATTLE_REQUEST,new GsStartWorldBossBattleRequest{isCombo=combo,bossId=id});
                string active=State();await peer.Request<FailPacket>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,end,success:false);Check(State()==active,"Late prior settlement cannot replace newer world encounter: "+mode);
                await peer.Request<FailPacket>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,end,retry:true,success:false);Check(State()==active,"Cached prior settlement acknowledgement cannot overwrite a newer battle: "+mode);
                var retreat=new LocalRankCombatRequest{operation=10,mode=5,stamp=(ulong)next.timeStamp};await peer.Request<LocalRankCombatReply>(GameServerCommand.GS_GET_RANK_BATTLE_RECORD_REQUEST,retreat);
                Check(p.ActiveStage==null&&p.Raids.ActiveStamp==0&&p.Raids.Used[mode]==used+1&&p.Raids.Latest[mode]>=latest&&p.WorldBossPoint==result.nowCoinInfo.worldBossPoint,"Token-bound world retreat preserves entry, cooldown and rewards: "+mode);
                string idle=State();await peer.Request<FailPacket>(GameServerCommand.GS_END_WORLD_BOSS_BATTLE_REQUEST,new GsEndWorldBossBattleRequest{bossId=id,isCombo=combo,timeStamp=next.timeStamp,damageAmount=1,battleEndSyncData=new(),battleHistory=new()},success:false);Check(State()==idle,"Retreated world battle cannot subsequently settle: "+mode);
                await peer.Request<GsGetWorldBossInfoOk>(GameServerCommand.GS_GET_WORLD_BOSS_INFO_REQUEST,new GsGetWorldBossInfoRequest());Check(State()==idle,"Idle world info does not change admission or progress: "+mode);
            }
            r.Store.Save();
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/world-raid-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant()},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("WORLD_RAID_TEST_OK "+checks.Count);
    }
}
