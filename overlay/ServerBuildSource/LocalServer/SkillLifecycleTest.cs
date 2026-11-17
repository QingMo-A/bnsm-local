extern alias game;
using ProtoBuf;
using System.Text.Json;
using System.Net.Sockets;

internal static class SkillLifecycleTest
{
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    public static async Task Run(string root)
    {
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        string save=Path.Combine(root,"verification/skill-lifecycle",Guid.NewGuid().ToString("N"));
        await using var runtime=new LocalRuntime(root,save,0,0,0,false);
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        async Task<(ushort Command,byte[] Payload)> Send(TcpClient socket,ushort command,object request) {
            await socket.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));
            using var deadline=new CancellationTokenSource(10000);
            var decoded=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),deadline.Token))!,true);
            return(decoded.Command,decoded.Payload);
        }
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",runtime.LoginPort);
        await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="skill-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload);
        var key=new SessionKeyInfo{uid=start.uid,loginSessionKey=start.loginSessionKey};
        using var gameSocket=new TcpClient();await gameSocket.ConnectAsync("127.0.0.1",runtime.GamePort);
        async Task<byte[]> Upgrade(GsCardSkillLevelUpRequest request,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;
            var reply=await Send(gameSocket,(ushort)GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,request);
            if(reply.Command!=(ushort)(success?GameServerCommand.GS_CARD_SKILL_LEVELUP_OK:GameServerCommand.GS_CARD_SKILL_LEVELUP_FAIL))throw new Exception("Unexpected skill reply: "+Read<FailPacket>(reply.Payload).message);
            return reply.Payload;
        }
        var c=runtime.Catalog;var p=runtime.Store.Find(key.uid)!;
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.ActiveStage=null;
        int[] uiIndex=[0,4,1,2,3,5,6];
        BscTable.NUMBER_TYPE[] unlock=[BscTable.NUMBER_TYPE.ACTIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.ULTIMATE_SKILL_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_2_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_3_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_4_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_5_OPEN_GRADE];
        var heroes=c.UsableHeroes.ToArray();
        var missing=heroes.SelectMany(card=>card.ActiveSkillId.Concat(card.PassiveSkillId).Where(id=>id>0&&!c.SkillIds.Contains(id)).Select(id=>new{card.Id,skillId=id})).ToArray();
        Check(missing.Length==0,"Every nonzero skill of all "+heroes.Length+" collectible card records exists in the same-version skill table");
        var representatives=heroes.GroupBy(x=>x.Rank).Select(g=>g.OrderByDescending(x=>x.PassiveSkillId.Count(id=>id>0)).First()).ToList();
        foreach(var card in representatives) {
            for(int slot=0;slot<7;slot++) {
                var ids=slot<2?card.ActiveSkillId:card.PassiveSkillId;int index=slot<2?slot:slot-2;
                if(index>=ids.Count||!c.SkillIds.Contains(ids[index]))continue;
                p.CardIds[0]=(uint)card.Id;p.CardStates[1]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99)};
                p.Gold=10000000;p.SkillPoint=c.SkillPointMax(p);p.SkillPointStamp=ProtocolRouter.Now;
                var request=new GsCardSkillLevelUpRequest{keyInfo=key,targetCardSeq=1};request.levelUpInfo.Add(new(){skillType=(CardSkillType)slot,levelupValue=1});
                bool open=card.Rank>=c.Numbers[unlock[slot]];string label=$"rank {card.Rank} slot {slot}";
                var bytes=await Upgrade(request,open);
                if(open) {
                    var result=Read<GsCardSkillLevelUpOk>(bytes);var levels=slot<2?result.cardInfo.activeSkillLevel:result.cardInfo.nonActiveSkillLevel;
                    uint cost=(uint)c.CardProcesses.GetSkillItem(uiIndex[slot]).ProcessDataList[0].CostGold;
                    Check(levels[index]==2&&result.currentCoinInfo.gold==10000000-cost&&result.remainSkillPoint==c.SkillPointMax(p)-1,label+" uses its own original cost and packet slot");
                    uint gold=p.Gold;await Upgrade(request,retry:true);Check(p.Gold==gold,label+" replay is idempotent");
                } else Check(p.Gold==10000000&&p.CardStates[1].ActiveSkills.All(x=>x==1)&&p.CardStates[1].PassiveSkills.All(x=>x==1),label+" remains locked without deductions");
            }
        }
        var high=heroes.Where(x=>x.Rank==8&&x.PassiveSkillId.Count>0).First();p.CardIds[0]=(uint)high.Id;
        p.CardStates[1]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),ActiveSkills=[99,1],PassiveSkills=[49,1,1,1,1]};
        p.Gold=10000000;p.SkillPoint=3;p.SkillPointStamp=ProtocolRouter.Now;
        var second=new GsCardSkillLevelUpRequest{keyInfo=key,targetCardSeq=1};second.levelUpInfo.Add(new(){skillType=CardSkillType.CARD_SKILL_NON_ACTIVE_ONE,levelupValue=2});
        uint expected=(uint)c.CardProcesses.GetSkillItem(1).ProcessDataList.Skip(48).Take(2).Sum(x=>x.CostGold);
        var upgraded=Read<GsCardSkillLevelUpOk>(await Upgrade(second));
        Check(upgraded.cardInfo.nonActiveSkillLevel[0]==51&&upgraded.cardInfo.activeSkillLevel[0]==99&&p.Gold==10000000-expected&&p.SkillPoint==1,"Second displayed skill crosses 49, 50 and 51 without transcendence and without changing the first skill");
        string State()=>JsonSerializer.Serialize(new{p.Gold,p.SkillPoint,p.CardStates});
        p.SkillPoint=0;p.SkillPointStamp=ProtocolRouter.Now;string before=State();second.levelUpInfo[0].levelupValue=1;
        var fail=Read<FailPacket>(await Upgrade(second,false));Check(State()==before&&fail.errorCode==(uint)ServerErrorCode.ERROR_NOT_ENOUGH_SKILL_POINT,"Zero skill points reject atomically with the original failure code");
        p.SkillPoint=3;second.levelUpInfo.Add(new(){skillType=CardSkillType.CARD_SKILL_ACTIVE_ONE,levelupValue=1});before=State();await Upgrade(second,false);
        Check(State()==before,"Batch containing a max-level first skill cannot partially charge the second skill");
        second.levelUpInfo.RemoveAt(1);p.CardStates[1].PassiveSkills[0]=98;await Upgrade(second);before=State();await Upgrade(second,false);
        Check(State()==before&&p.CardStates[1].PassiveSkills[0]==99,"Second skill reaches 99 and cannot exceed the card level or original cap");
        runtime.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.CardStates[1].PassiveSkills[0]==99&&restored.Gold==p.Gold,"Upgraded skills and wallet survive save reload");
        File.WriteAllText(Path.Combine(root,"evidence/skill-lifecycle-server-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("SKILL_LIFECYCLE_PASS "+checks.Count);
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated skill fixture required");
        Directory.CreateDirectory(output);
        await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("local-player");
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
            var entry=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN"));
            using var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",entry.GetProperty("name").GetString()+".pb"));
            var texts=Serializer.Deserialize<BscTable.LocalTextDataTable>(input);texts.MakeMap();
            var qin=c.UsableHeroes.Where(x=>(texts.GetItem(x.NameAlias)?.Text??"").Contains("大魔王")&&x.Rank==8).OrderBy(x=>x.Id).First();
            File.WriteAllText(Path.Combine(output,"original-skills.json"),JsonSerializer.Serialize(new{qin.Id,name=texts.GetItem(qin.NameAlias)?.Text,qin.Rank,qin.ActiveSkillId,qin.PassiveSkillId,open=c.Numbers.Where(x=>x.Key.ToString().Contains("SKILL")||x.Key.ToString().Contains("TRANSCENDENCE"))},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=100000000;p.SkillPoint=2;p.SkillPointStamp=ProtocolRouter.Now;p.CashPoint=1000000;
            p.CardIds[0]=(uint)qin.Id;p.CardStates[1]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=7,ActiveSkills=[99,1],PassiveSkills=[50,17,91,1,1]};
            var full=c.UsableHeroes.Where(x=>x.PassiveSkillId.Count==4&&x.PassiveSkillId.All(c.SkillIds.Contains)&&x.Rank>=c.Numbers[BscTable.NUMBER_TYPE.PASSIVE_SKILL_4_OPEN_GRADE]).OrderBy(x=>x.Rank).First();
            p.CardIds.Add((uint)full.Id);p.CardStates[(ulong)p.CardIds.Count]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99)};
            var recovery=c.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_SKILL_POINT&&x.RecoveryValue>0);p.Inventory[(uint)recovery.Id]=2;
            ulong day=(ProtocolRouter.Now+8*3600)/86400;
            p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
            runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
}
