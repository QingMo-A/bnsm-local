using System.Net.Sockets;
using System.Text.Json;

internal static class EvilWarTest
{
    private sealed class Peer : IDisposable
    {
        private readonly TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(TcpClient client,ushort command,object request) {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server,string account) {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false) {
            if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)command,body);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)r.Item1+": "+Read<FailPacket>(r.Item2).message);
            return Read<T>(r.Item2);
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;

    public static async Task Fixture(string root,string output,bool early=false)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated infinite challenge fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            var p=runtime.Store.GetOrCreate("local-player");p.Level=99;p.Exp=ProgressionRules.PlayerFloor(runtime.Catalog,99);p.ClearStage=179;
            p.EvilWar=new(){Stage=early?0u:99u,BestStage=early?0u:99u,Slots=[5000,5000,5000,5000,5000],EvilStone=1e25,HolyStone=10000};
            var rules=new EvilWarRules(runtime.Catalog);
            p.EvilWar.EvilStone=EvilWarRules.RoundCost(Enumerable.Range(0,32).Sum(i=>rules.SlotCost(5000+(uint)i)));
            ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(12));
    }

    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/evil-war",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;var rules=new EvilWarRules(c);
            string State()=>JsonSerializer.Serialize(new{p.EvilWar,p.Moonstone});
            async Task Bad(GameServerCommand cmd,object request,string label) {string before=State();await peer.Request<FailPacket>(cmd,request,false);Check(before==State(),label+" is rejected without changing state");}
            GsEvilWarEnterRequest Enter()=>new(){keyInfo=peer.Key};
            GsEvilWarBattleEndRequest End(uint stage) {var r=new GsEvilWarBattleEndRequest{keyInfo=peer.Key,currentStageId=stage};r.slotLevelVec.AddRange(p.EvilWar.Slots);return r;}
            GsEvilWarSlotEnchantRequest Upgrade(uint delta) {var r=new GsEvilWarSlotEnchantRequest{keyInfo=peer.Key,currentStageId=p.EvilWar.Stage};r.slotLevelVec.AddRange(p.EvilWar.Slots);r.slotLevelVec[0]+=delta;return r;}
            Check(c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EVIL_WAR)==70,"Original unlock level is 70");
            await Bad(GameServerCommand.GS_EVIL_WAR_ENTER_REQUEST,Enter(),"Entry below level 70");p.Level=70;p.Exp=ProgressionRules.PlayerFloor(c,70);
            await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(1),"Settlement before entry");
            var entered=await peer.Request<GsEvilWarEnterOk>(GameServerCommand.GS_EVIL_WAR_ENTER_REQUEST,Enter());
            Check(entered.record.currentStageId==0&&entered.record.slotLevelVec.SequenceEqual(new uint[]{1,1,1,1,1})&&entered.record.evilStone!=null&&entered.record.holyStone!=null&&entered.earnedEvilStone!=null,"Fresh entry returns a complete native record and five level-one fighters");
            await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(2),"Skipping a wave");await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(0),"Wave zero reward");
            await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(1),"Unfunded power upgrade");
            var draw=new GsEvilWarRuneGachaRequest{keyInfo=peer.Key};await Bad(GameServerCommand.GS_EVIL_WAR_RUNE_GACHA_REQUEST,draw,"Unfunded rune draw");
            var first=End(1);var result=await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,first);
            Check(p.EvilWar.Stage==1&&p.EvilWar.EvilStone==300&&result.earnedEvilStone.@base==300,"First wave grants original formula 300 purple crystals");
            string state=State();await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,first,retry:true);Check(State()==state,"Same packet retry cannot duplicate wave rewards");
            first.currentStageId=2;await peer.Request<FailPacket>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,first,false,true);Check(State()==state,"Same sequence with modified payload is rejected");
            await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(1),"Replaying a non-farming wave");
            var up=Upgrade(1);await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,up);Check(p.EvilWar.Slots[0]==2&&p.EvilWar.EvilStone==299,"Power upgrade consumes original level-one cost");
            state=State();await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,up,retry:true);Check(state==State(),"Power upgrade retry is idempotent");
            await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(101),"Batch above one hundred");
            var downgrade=Upgrade(1);downgrade.slotLevelVec[0]=1;await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,downgrade,"Power downgrade");
            var forged=End(2);forged.slotLevelVec[0]=900;await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,forged,"Forged power levels");
            for(uint wave=2;wave<=9;wave++)await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(wave));
            double evil=p.EvilWar.EvilStone;await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(9));Check(p.EvilWar.Stage==9&&p.EvilWar.EvilStone==evil+rules.EvilDrop(9,p.EvilWar),"Pre-boss repeat farming grants crystals without advancing");
            var boss=await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(10));
            Check(p.EvilWar.BestStage==10&&p.EvilWar.HolyStone==rules.HolyDrop(10)&&p.Moonstone==90&&boss.earnedMoonStone==90,"First boss grants original sapphire formula and guaranteed 90 gold stones");
            Check(p.CardIds.Count==5&&p.Gold==10000&&p.Exp==ProgressionRules.PlayerFloor(c,70),"Idle progression is independent of normal card and player growth");
            await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(10),"Replaying a cleared boss");
            await Bad(GameServerCommand.GS_EVIL_WAR_RESET_REQUEST,new GsEvilWarResetRequest{keyInfo=peer.Key,currentStageId=10},"Rebirth below 100 waves");
            double holy=p.EvilWar.HolyStone;var rolled=await peer.Request<GsEvilWarRuneGachaOk>(GameServerCommand.GS_EVIL_WAR_RUNE_GACHA_REQUEST,draw);uint rune=rolled.record.candidateRuneId;
            Check(rune>=1&&rune<=6&&p.EvilWar.HolyStone==holy-10&&p.EvilWar.Runes.Count==0,"Rune draw charges ten sapphires and holds an unowned candidate");
            state=State();await peer.Request<GsEvilWarRuneGachaOk>(GameServerCommand.GS_EVIL_WAR_RUNE_GACHA_REQUEST,draw,retry:true);Check(state==State(),"Rune draw retry preserves candidate and balance");
            var confirm=new GsEvilWarRuneEnchantRequest{keyInfo=peer.Key,currentStageId=10,runeId=rune==6?1u:rune+1};await Bad(GameServerCommand.GS_EVIL_WAR_RUNE_ENCHANT_REQUEST,confirm,"Accepting an unrolled rune");
            confirm.runeId=rune;holy=p.EvilWar.HolyStone;await peer.Request<GsEvilWarRuneEnchantOk>(GameServerCommand.GS_EVIL_WAR_RUNE_ENCHANT_REQUEST,confirm);Check(p.EvilWar.Candidate==0&&p.EvilWar.Runes[rune]==1&&p.EvilWar.HolyStone==holy,"Candidate confirmation is free and adds a level-one rune");
            state=State();await peer.Request<GsEvilWarRuneEnchantOk>(GameServerCommand.GS_EVIL_WAR_RUNE_ENCHANT_REQUEST,confirm,retry:true);Check(state==State(),"Rune confirmation retry cannot turn into a paid upgrade");
            p.EvilWar.HolyStone=100000;holy=p.EvilWar.HolyStone;await peer.Request<GsEvilWarRuneEnchantOk>(GameServerCommand.GS_EVIL_WAR_RUNE_ENCHANT_REQUEST,confirm);Check(p.EvilWar.Runes[rune]==2&&p.EvilWar.HolyStone==holy-20,"Owned rune upgrades use original 20-sapphire first cost");
            p.EvilWar.EvilStone=1e25;await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(10));Check(p.EvilWar.Slots[0]==12,"Batch-of-ten power upgrade is supported");
            await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(100));Check(p.EvilWar.Slots[0]==112,"Batch-of-one-hundred power upgrade is supported");
            foreach(uint delta in new uint[]{2,26,99}) {
                uint previous=p.EvilWar.Slots[0];double balance=p.EvilWar.EvilStone,power=rules.Power(p.EvilWar);
                double cost=EvilWarRules.RoundCost(Enumerable.Range(0,(int)delta).Sum(i=>rules.SlotCost(previous+(uint)i)));
                var partial=Upgrade(delta);var upgraded=await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,partial);
                Check(p.EvilWar.Slots[0]==previous+delta&&upgraded.record.slotLevelVec.SequenceEqual(p.EvilWar.Slots)&&p.EvilWar.EvilStone==balance-cost&&rules.Power(p.EvilWar)>power,"Native affordable batch +"+delta+" applies exact levels, cost and power");
                state=State();await peer.Request<GsEvilWarSlotEnchantOk>(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,partial,retry:true);Check(State()==state,"Partial batch retry does not charge or upgrade twice: "+delta);
            }
            await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(0),"Zero-level upgrade");
            var multi=Upgrade(2);multi.slotLevelVec[1]++;await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,multi,"Multi-character upgrade");
            for(uint wave=11;wave<=100;wave++)await peer.Request<GsEvilWarBattleEndOk>(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(wave));
            uint moon=p.Moonstone;holy=p.EvilWar.HolyStone;var runes=new Dictionary<uint,uint>(p.EvilWar.Runes);
            var reset=new GsEvilWarResetRequest{keyInfo=peer.Key,currentStageId=100};await peer.Request<GsEvilWarResetOk>(GameServerCommand.GS_EVIL_WAR_RESET_REQUEST,reset);
            Check(p.EvilWar.Stage==0&&p.EvilWar.BestStage==100&&p.EvilWar.Resets==1&&p.EvilWar.EvilStone==0&&p.EvilWar.Slots.All(x=>x==1),"Rebirth resets only current waves, purple crystals and idle levels");
            Check(p.EvilWar.HolyStone==holy+rules.ResetReward(100)&&p.Moonstone==moon&&p.EvilWar.Runes.SequenceEqual(runes),"Rebirth preserves gold stones and runes and grants original reset formula");
            state=State();await peer.Request<GsEvilWarResetOk>(GameServerCommand.GS_EVIL_WAR_RESET_REQUEST,reset,retry:true);Check(state==State(),"Rebirth retry cannot duplicate its reward");
            for(uint id=1;id<=6;id++)p.EvilWar.Runes[id]=1;await Bad(GameServerCommand.GS_EVIL_WAR_RUNE_GACHA_REQUEST,draw,"Draw after all six runes are owned");
            p.EvilWar.Slots[0]=99999;await Bad(GameServerCommand.GS_EVIL_WAR_SLOT_ENCHANT_REQUEST,Upgrade(1),"Exceeding maximum idle level");p.EvilWar.Slots[0]=1;
            p.EvilWar.AccountedAt=ProtocolRouter.Now-3600;evil=p.EvilWar.EvilStone;entered=await peer.Request<GsEvilWarEnterOk>(GameServerCommand.GS_EVIL_WAR_ENTER_REQUEST,Enter());Check(p.EvilWar.EvilStone>evil&&entered.earnedEvilStone.@base>0&&p.EvilWar.Stage==0,"Offline claim grants crystals without inventing cleared waves");
            state=State();await peer.Request<GsEvilWarEnterOk>(GameServerCommand.GS_EVIL_WAR_ENTER_REQUEST,Enter(),retry:true);Check(state==State(),"Offline claim retry cannot double rewards");
            var sample=new LocalEvilWar{AccountedAt=1000,Stage=99};Check(rules.Offline(sample,1000+259200)==rules.Offline(sample,1000+259200*4),"Offline reward is capped at original three days");Check(rules.Offline(sample,999)==0,"Clock rollback cannot generate offline rewards");
            Check(EvilWarRules.Pack(EvilWarRules.MaxNumber).@base>0&&EvilWarRules.Pack(EvilWarRules.MaxNumber).exponent<=72,"Large currencies remain representable at native cap");
            var exit=new GsEvilWarExitRequest{keyInfo=peer.Key,currentStageId=0};exit.slotLevelVec.AddRange(p.EvilWar.Slots);await peer.Request<GsEvilWarExitOk>(GameServerCommand.GS_EVIL_WAR_EXIT_REQUEST,exit);Check(p.EvilWar.ActiveSession.Length==0,"Exit saves checkpoint and closes active session");
            await Bad(GameServerCommand.GS_EVIL_WAR_BATTLE_END_REQUEST,End(1),"Settlement after exit");
            p.EvilWar.ExtraFields=new(){["futureIdleFeature"]=JsonSerializer.SerializeToElement(123)};server.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(restarted,"local-player");var entered=await peer.Request<GsEvilWarEnterOk>(GameServerCommand.GS_EVIL_WAR_ENTER_REQUEST,new GsEvilWarEnterRequest{keyInfo=peer.Key});
            Check(entered.record.resetCount==1&&entered.record.bestStageId==100&&entered.record.runeDataVec.Count==6&&entered.record.evilStone.@base>0,"Server restart retains rebirths, highest wave, runes and currencies");Check(restarted.Store.Find(peer.Key.uid)!.EvilWar.ExtraFields!["futureIdleFeature"].GetInt32()==123,"Unknown future save fields survive updates");
        }
        File.WriteAllText(Path.Combine(root,"evidence/evil-war-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Infinite challenge: {checks.Count}/{checks.Count}");
    }
}
