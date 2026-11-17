using System.Net.Sockets;
using System.Text.Json;
internal static class TranscendenceTest
{
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    public static void Prepare(LocalRuntime runtime)
    {
        var c=runtime.Catalog;var p=runtime.Store.GetOrCreate("local-player");
        var rule=c.Transcendence.First(x=>c.UsableHeroes.Any(card=>card.Id==x.CardId));
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=1000000;p.CashPoint=100000;
        p.CardIds[0]=(uint)rule.CardId;p.CardStates[1]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99)};
        p.Inventory[(uint)rule.GrowthItemId]=100000;
        foreach(var grade in rule.EffectDataList.SelectMany(x=>x.EffectGradeDataList).Where(x=>x.GradeItemId>0))p.Inventory[(uint)grade.GradeItemId]=100000;
        runtime.Store.Save();
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/transcendence",Guid.NewGuid().ToString("N"));await using var runtime=new LocalRuntime(root,save,0,0,0,false);Prepare(runtime);
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        async Task<(ushort,byte[])> Send(TcpClient socket,ushort command,object body){await socket.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(body),true));using var timeout=new CancellationTokenSource(10000);var r=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);}
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",runtime.LoginPort);
        await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);
        var key=new SessionKeyInfo{uid=start.uid,loginSessionKey=start.loginSessionKey};using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",runtime.GamePort);
        var p=runtime.Store.Find(key.uid)!;var rule=runtime.Catalog.Transcendence.First(x=>x.CardId==p.CardIds[0]);var state=p.CardStates[1];
        var checks=new List<string>();void Check(bool yes,string label){if(!yes)throw new Exception(label);checks.Add(label);}
        async Task<byte[]> Request(GameServerCommand command,object body,bool success=true,bool retry=false){if(!retry)key.sequenceNo++;var response=await Send(socket,(ushort)command,body);string expect=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");if(((GameServerCommand)response.Item1).ToString()!=expect)throw new Exception("Expected "+expect+", got "+(GameServerCommand)response.Item1);return response.Item2;}
        string State()=>JsonSerializer.Serialize(new{p.Inventory,p.CardStates,p.TranscendenceReceipts});
        var growth=new GsApplyTranscendenceGrowthRequest{keyInfo=key,cardSeq=1};var cancel=new GsCancelTranscendenceGrowthRequest{keyInfo=key,cardSeq=1};var info=new GsGetTranscendenceInfoRequest{keyInfo=key,cardSeq=1};
        var initial=Read<GsGetTranscendenceInfoOk>(await Request(GameServerCommand.GS_GET_TRANSCENDENCE_INFO_REQUEST,info));Check(initial.cardInfo.effectDataInfoList.Count==0,"Untrained supported card loads without inventing attributes");
        string before=State();await Request(GameServerCommand.GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST,cancel,false);Check(State()==before,"Cancel without growth does not charge or grant materials");
        growth.cardSeq=999;before=State();await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,false);Check(State()==before,"Unowned card rejects atomically");growth.cardSeq=1;
        uint realId=p.CardIds[0];p.CardIds[0]=runtime.Catalog.StarterCards()[0];before=State();await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,false);Check(State()==before,"Original star-tier gate rejects lower star cards");p.CardIds[0]=realId;
        p.Inventory[(uint)rule.GrowthItemId]=0;before=State();await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,false);Check(State()==before,"Insufficient growth materials cannot alter attributes");p.Inventory[(uint)rule.GrowthItemId]=100000;
        var result=Read<GsApplyTranscendenceGrowthOk>(await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth));
        Check(result.cardInfo.effectDataInfoList.Count==3&&p.Inventory[(uint)rule.GrowthItemId]==100000-rule.GrowthItemValue,"Growth unlocks base attributes and charges original material count");
        Check(state.TranscendenceEffects.All(x=>x.Grade==1&&x.Value>=1&&x.Value<=71),"Initial growth obeys original weighted ranges and lower bound");
        Check(result.consumableItemDeltaList.Single().itemCount==-rule.GrowthItemValue,"Protocol carries exact consumed-item delta");
        before=State();await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,retry:true);Check(State()==before,"Retransmitted growth neither rerolls nor charges twice");
        growth.cardSeq=2;await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,false,true);Check(State()==before,"Sequence reuse with changed card cannot mutate state");growth.cardSeq=1;
        runtime.Store.Save();var restored=new PlayerStore(save,runtime.Catalog).Find(p.Uid)!;
        Check(restored.CardStates[1].TranscendenceEffects.Count==3&&restored.CardStates[1].TranscendenceUndo!=null,"Attributes and rollback snapshot survive disk reload");
        var undone=Read<GsCancelTranscendenceGrowthOk>(await Request(GameServerCommand.GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST,cancel));
        Check(undone.cardInfo.effectDataInfoList.Count==0&&p.Inventory[(uint)rule.GrowthItemId]==100000-rule.GrowthCancelItemValue,"Cancel restores attributes, refunds growth and charges original cancel fee");
        before=State();await Request(GameServerCommand.GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST,cancel,retry:true);Check(State()==before,"Cancel retransmission cannot duplicate refund");
        var effectRule=rule.EffectDataList[0];state.TranscendenceEffects=[new(){Id=(uint)effectRule.Id,Grade=1,Value=1}];var breakthrough=new GsBreakThroughTranscendenceGrowthRequest{keyInfo=key,cardSeq=1,effectDataId=(uint)effectRule.Id};
        before=State();await Request(GameServerCommand.GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST,breakthrough,false);Check(State()==before,"Breakthrough requires current grade to be full");
        state.TranscendenceEffects[0].Value=(uint)effectRule.EffectGradeDataList[0].EffectMax;int held=p.Inventory[(uint)effectRule.EffectGradeDataList[1].GradeItemId];
        var broken=Read<GsBreakThroughTranscendenceGrowthOk>(await Request(GameServerCommand.GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST,breakthrough));
        Check(broken.cardInfo.effectDataInfoList.Single().effectGrade==2&&p.Inventory[(uint)effectRule.EffectGradeDataList[1].GradeItemId]==held-effectRule.EffectGradeDataList[1].GradeValue,"Breakthrough raises exactly one grade at original cost");
        Check(effectRule.EffectGradeDataList[1].GradeSuccessBouns.Contains((int)state.TranscendenceEffects[0].Value-effectRule.EffectGradeDataList[0].EffectMax),"Breakthrough bonus comes from original weighted outcomes");
        before=State();await Request(GameServerCommand.GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST,breakthrough,retry:true);Check(State()==before,"Breakthrough retransmission cannot double-spend");
        state.TranscendenceEffects=rule.EffectDataList.Take(3).Select(x=>new LocalTranscendenceEffect{Id=(uint)x.Id,Grade=3,Value=1}).ToList();
        await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth);Check(state.TranscendenceEffects.Count==5,"Two grade-three attributes unlock the remaining original attributes");
        state.TranscendenceEffects=rule.EffectDataList.Select(x=>new LocalTranscendenceEffect{Id=(uint)x.Id,Grade=(uint)x.EffectGradeDataList.Count,Value=(uint)x.EffectGradeDataList.Last().EffectMax}).ToList();
        before=State();await Request(GameServerCommand.GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST,growth,false);Check(State()==before,"All-max attributes cannot consume growth items");
        await Request(GameServerCommand.GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST,breakthrough,false);Check(State()==before,"Final grade cannot break through again");
        runtime.Store.Save();restored=new PlayerStore(save,runtime.Catalog).Find(p.Uid)!;Check(restored.CardStates[1].TranscendenceEffects.All(x=>x.Grade==5)&&restored.Inventory.SequenceEqual(p.Inventory),"Final attributes and inventory persist together");
        File.WriteAllText(Path.Combine(root,"evidence/transcendence-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("TRANSCENDENCE_PASS "+checks.Count);
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated mobile-work verification folder required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){TrainingTest.Prepare(runtime);Prepare(runtime);}
        await using var app=await DebugToolsTest.Host(runtime);
        app.MapPost("/fixture/transcendence/fill",()=>{
            lock(runtime.Store.Gate){var p=runtime.Store.GetOrCreate("local-player");var rule=runtime.Catalog.Transcendence.First(x=>x.CardId==p.CardIds[0]);var s=p.CardStates[1];s.TranscendenceEffects[0].Value=(uint)rule.EffectDataList[0].EffectGradeDataList[0].EffectMax;s.TranscendenceUndo=null;runtime.Store.Save();return Results.Json(new{ok=true});}
        });
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(12));
    }
}
