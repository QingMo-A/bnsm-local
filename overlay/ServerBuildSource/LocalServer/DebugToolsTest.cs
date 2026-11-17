using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

internal static class DebugToolsTest
{
    public static void Prepare(LocalRuntime runtime)
    {
        var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("local-player");
        p.Level=20;p.Exp=ProgressionRules.PlayerFloor(c,20);p.Gold=10000;p.CashPoint=10000;p.VipExp=0;
        p.Stamina=5000;p.ClearStage=179;p.StageRecords[13]=new(){Stars=3};
        p.CardStates[1]=new(){Level=5,Exp=ProgressionRules.CardFloor(c,5)};
        foreach(var item in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GOLDITEM))p.Inventory[(uint)item.Id]=3;
        p.Inventory[210002001]=100000;
        ulong now=ProtocolRouter.Now,day=(now+8*3600)/86400;
        p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.AttendanceLastTime=now;p.ShortTermDays=1;p.ShortTermLastDay=day;
        runtime.Store.Save();
    }
    public static async Task<WebApplication> Host(LocalRuntime runtime)
    {
        var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app=builder.Build();DebugApi.Map(app,runtime);await app.StartAsync();return app;
    }
    private static string Address(WebApplication app)=>app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);Prepare(runtime);
        await using var app=await Host(runtime);
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new {directoryPort=runtime.DirectoryPort,debugPort=new Uri(Address(app)).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/debug-tools",Guid.NewGuid().ToString("N"));
        await using var runtime=new LocalRuntime(root,save,0,0,0,false);Prepare(runtime);var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("local-player");
        await using var app=await Host(runtime);using var http=new HttpClient{BaseAddress=new Uri(Address(app))};
        var checks=new List<string>();void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        async Task<JsonElement> Post(string path,object body) {using var r=await http.PostAsJsonAsync(path,body);r.EnsureSuccessStatusCode();return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
        bool Ok(JsonElement e)=>e.GetProperty("ok").GetBoolean();
        var connection=new ConnectionState{Player=p};
        runtime.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,WireCodec.Serialize(new LsLoginRequest{userID="local-player",userPW="local"}),connection);
        var login=(LsGameStartOk)runtime.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,WireCodec.Serialize(new LsGameStartRequest{serverId=1,bAgreement=true}),connection).Message!;
        var key=new SessionKeyInfo{uid=p.Uid,loginSessionKey=login.loginSessionKey};
        T Request<T>(GameServerCommand cmd,object body,bool success=true,bool advance=true) {
            if(advance)key.sequenceNo++;
            var reply=runtime.Router.Handle(ServiceKind.Game,(ushort)cmd,WireCodec.Serialize(body),connection);
            string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(reply.Name!=expected)throw new Exception("Expected "+expected+", got "+reply.Name+": "+(reply.Message as FailPacket)?.message);
            return (T)reply.Message!;
        }
        Check(Ok(await Post("/debug/level",new {level=30}))&&p.Level==30&&p.Exp==ProgressionRules.PlayerFloor(c,30),"Player debug level stores the matching original cumulative EXP");
        var sweep=new GsNormalBattleSkipRequest{keyInfo=key,stageId=13,skipCount=1,difficulty=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL};
        uint exp=p.Exp;var swept=Request<GsNormalBattleSkipOk>(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,sweep);
        Check(p.Level>=30&&p.Exp>exp&&swept.level==p.Level,"Real stage sweep EXP cannot roll back the debug player level");
        exp=p.Exp;await Post("/debug/level",new {level=p.Level});Check(p.Exp==exp,"Setting the same player level preserves partial EXP progress");
        await Post("/debug/level",new {level=20});Check(p.Level==20&&p.Exp==ProgressionRules.PlayerFloor(c,20),"Explicit player level reduction resets EXP to the chosen level floor");
        await Post("/debug/level",new {level=30});
        Check(Ok(await Post("/debug/card-exp",new {seq=1,addExp=100})),"Debug card EXP increase accepted");
        uint cardLevel=p.CardStates[1].Level,cardExp=p.CardStates[1].Exp;
        var trained=Request<GsCardLevelUpOk>(GameServerCommand.GS_LEVELUP_CARD_REQUEST,new GsCardLevelUpRequest{keyInfo=key,targetCardSeq=1,targetExp=cardExp+1});
        Check(trained.levelupCardInfo.cardLevel>=cardLevel&&p.CardStates[1].Exp==cardExp+1,"Original paid card upgrade continues from debug EXP and does not regress");
        await Post("/debug/card-exp",new {seq=1,exp=uint.MaxValue});
        int max=ProgressionRules.CardMax(c,p,c.Cards.First(x=>x.Id==p.CardIds[0]));
        Check(p.CardStates[1].Level==max&&p.CardStates[1].Exp==ProgressionRules.CardCeiling(c,max),"Debug max-card uses the same original cap as regular training");
        await Post("/debug/level",new {level=1});cardExp=p.CardStates[1].Exp;cardLevel=p.CardStates[1].Level;
        Check(!Ok(await Post("/debug/card-exp",new {seq=1,addExp=1}))&&p.CardStates[1].Exp==cardExp&&p.CardStates[1].Level==cardLevel,"EXP+ after lowering player level cannot silently reduce a trained card");
        await Post("/debug/level",new {level=30});
        p.ActiveStage=13;exp=p.Exp;
        Check(!Ok(await Post("/debug/level",new {level=10}))&&!Ok(await Post("/debug/card-exp",new {seq=1,exp=0}))&&p.Exp==exp,"Player/card debug changes are rejected during active battles");p.ActiveStage=null;
        Check(!Ok(await Post("/debug/card-exp",new {seq=1}))&&!Ok(await Post("/debug/card-exp",new {seq=999,exp=10})),"Missing EXP and unowned card debug requests are rejected");
        await Post("/debug/vip",new {addLevels=1});Check(c.VipFor(p.VipExp).VipLv==1,"VIP+ uses the original VIP EXP threshold");
        await Post("/debug/vip",new {level=20});Check(c.VipFor(p.VipExp).VipLv==20&&p.PendingCharge==null&&p.PurchasedVipPackages.Count==0,"V20 changes original privileges without creating recharge orders or claiming gifts");
        uint vipExp=p.VipExp;await Post("/debug/vip",new {addLevels=1});Check(p.VipExp==vipExp,"VIP increment at V20 preserves EXP");
        uint gold=p.Gold,cash=p.CashPoint;
        await Post("/debug/currency",new {currency="gold",amount=1000000});await Post("/debug/currency",new {currency="cash",amount=1000000});
        Check(p.Gold==gold+1000000&&p.CashPoint==cash+1000000,"Million gold and diamond shortcuts add exactly the requested amounts");
        gold=p.Gold;
        var concurrent=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Post("/debug/currency",new {currency="gold",amount=1})));
        Check(concurrent.All(Ok)&&p.Gold==gold+12,"Concurrent HTTP currency additions share the protocol save lock without losing updates");
        var info=Request<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest{keyInfo=key});
        Check(info.namecardInfo.commonInfo.vipGrade==20&&info.coinInfo.gold==p.Gold&&info.coinInfo.cashPoint==p.CashPoint,"Original game-info reply reflects debug VIP and currency values");
        gold=p.Gold;Check(!Ok(await Post("/debug/currency",new {currency="gold",amount=uint.MaxValue}))&&p.Gold==gold,"Currency overflow rejects the entire debug addition");
        Check(!Ok(await Post("/debug/currency",new {currency="gold",amount=0}))&&!Ok(await Post("/debug/currency",new {currency="other",amount=10})),"Invalid debug currency additions do not mutate balances");
        var dolls=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GOLDITEM).ToArray();
        foreach(var doll in dolls) {
            gold=p.Gold;var sell=new GsSellUserItemRequest{keyInfo=key};sell.sellItemInfo.Add(new(){itemID=(uint)doll.Id,itemCount=2});
            var sold=Request<GsSellUserItemOk>(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,sell);
            Check(p.Gold==gold+2*doll.Value&&p.Inventory[(uint)doll.Id]==1&&sold.deltaItemList.Single().itemCount==-2,$"Doll {doll.Id} sells at original Value={doll.Value}, not Sell=1");
            gold=p.Gold;Request<GsSellUserItemOk>(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,sell,true,false);
            Check(p.Gold==gold&&p.Inventory[(uint)doll.Id]==1,$"Doll {doll.Id} sale retry cannot double-credit money");
        }
        var invalid=new GsSellUserItemRequest{keyInfo=key};invalid.sellItemInfo.Add(new(){itemID=(uint)dolls[0].Id,itemCount=1});invalid.sellItemInfo.Add(new(){itemID=(uint)dolls[1].Id,itemCount=2});
        gold=p.Gold;Request<FailPacket>(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,invalid,false);
        Check(p.Gold==gold&&p.Inventory[(uint)dolls[0].Id]==1,"Mixed oversell remains atomic with original sale prices");
        await Post("/debug/level",new {level=uint.MaxValue});
        Check(p.Level==ProgressionRules.PlayerMax(c)&&c.PlayerStates.Any(x=>x.Level==p.Level+1),"Max player level respects original limits and the client's next-level EXP row");
        runtime.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.Exp==p.Exp&&restored.Level==p.Level&&restored.VipExp==p.VipExp&&restored.Gold==p.Gold&&restored.CashPoint==p.CashPoint&&restored.CardStates[1].Exp==p.CardStates[1].Exp,"Debug progression VIP money and card EXP survive disk reload");
        p.Level=30;p.Exp=0;p.CardStates[1].Level=10;p.CardStates[1].Exp=0;runtime.Store.Save();restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.Level==30&&restored.Exp==ProgressionRules.PlayerFloor(c,30)&&restored.CardStates[1].Level==10&&restored.CardStates[1].Exp==ProgressionRules.CardFloor(c,10),"Old inconsistent debug saves repair EXP floors while retaining chosen levels");
        Check(Directory.GetFiles(save,"players.json.before-debug-exp-repair-*").Length==1,"Legacy EXP repair creates a complete save backup before migration");
        File.WriteAllText(Path.Combine(root,"evidence/debug-tools-test.json"),JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Debug tools and original sales: {checks.Count}/{checks.Count}");
    }
}
