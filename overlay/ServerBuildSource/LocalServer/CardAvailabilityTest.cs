using System.Net.Sockets;
using System.Text.Json;

internal static class CardAvailabilityTest
{
    private sealed class Peer(string root) : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();
        public byte[] Last=[];
        private async Task<(ushort,byte[])> Send(TcpClient client,ushort command,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));
            using var timeout=new CancellationTokenSource(10000);
            var response=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);
            return(response.Command,response.Payload);
        }
        public async Task Login(LocalRuntime runtime)
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",runtime.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",runtime.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object request,bool success=true,bool retry=false)
        {
            if(!retry)Key.sequenceNo++;var response=await Send(socket,(ushort)command,request);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)response.Item1).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(response.Item2).message);
            Last=response.Item2;return Read<T>(Last);
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    private static void Initialize(LocalPlayer p,GameCatalog c)
    {
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=10000000;p.CashPoint=1000000;
        p.Inventory[205081309]=2;p.Inventory[205081276]=2;p.Inventory[205081209]=2;
        ulong day=(ProtocolRouter.Now+8*3600)/86400;
        p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated card availability fixture required");
        Directory.CreateDirectory(output);
        await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){Initialize(runtime.Store.GetOrCreate("local-player"),runtime.Catalog);runtime.Store.Save();}
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/card-availability",Guid.NewGuid().ToString("N"));var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(runtime);var p=runtime.Store.Find(peer.Key.uid)!;var c=runtime.Catalog;Initialize(p,c);
            await peer.Request<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest{keyInfo=peer.Key});
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Inventory,p.CardIds,p.CardStates,p.CollectedCardIds,p.AdventureReceipts,p.GachaReceipts});
            GsCombinePieceRequest Combine(params uint[] ids){var r=new GsCombinePieceRequest{keyInfo=peer.Key};r.itemIdList.AddRange(ids);return r;}
            async Task Reject(uint[] ids,ServerErrorCode error,string label){string before=State();var failure=await peer.Request<FailPacket>(GameServerCommand.GS_COMBINE_PIECE_REQUEST,Combine(ids),false);Check(failure.errorCode==(uint)error&&State()==before,label+"; code="+failure.errorCode+" expected="+(uint)error+" unchanged="+(State()==before));}
            var family=c.UsableHeroes.Where(x=>x.EvolveFirstAncestor==50301).ToList();
            Check(family.Count==16&&family.All(x=>x.CollectionId==130&&x.Talent==BscTable.CARD_TALENT.CARD_TALENT_SS&&x.Minimi=="3DCH2127"),"All sixteen original SS Pizidao stages remain the same identity, collection and model");
            Check(c.DrawPool(true,4).Concat(c.DrawPool(false,4)).All(x=>x.EvolveFirstAncestor!=50301),"Original SS Pizidao remains available by fragments and is excluded from both card pools");
            Check(c.InitialHeroes.Any(x=>x.Id==50301&&x.PieceId==205081309),"The restored family remains registered for fragment summoning");
            Check(c.UsableHeroes.All(x=>x.EvolveFirstAncestor!=70031&&x.EvolveFirstAncestor!=10051&&x.EvolveFirstAncestor!=50101),"Zero-collection Sulinglong and Longwan plus X-template Pizidao stay excluded");
            Check(c.Pieces.Single(x=>x.ItemId==205081309).Complete==2&&c.Pieces.Single(x=>x.ItemId==205081309).NextId==50301,"Same-version original recipe consumes two fragments for initial card 50301");
            await Reject([205081276],ServerErrorCode.ERROR_DIFFERENCE_PIECE_DATA,"NPC Sulinglong summon is rejected without consuming fragments");
            await Reject([205081209],ServerErrorCode.ERROR_DIFFERENCE_PIECE_DATA,"NPC Longwan summon is rejected without consuming fragments");
            await Reject([205081309,205081276],ServerErrorCode.ERROR_DIFFERENCE_PIECE_DATA,"Mixed valid and NPC batch rejects the entire batch atomically");
            p.Inventory[205081309]=1;await Reject([205081309],ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT,"One Pizidao fragment cannot summon or change any resource");p.Inventory[205081309]=2;
            var request=Combine(205081309);uint gold=p.Gold,cash=p.CashPoint;int cards=p.CardIds.Count;
            var result=await peer.Request<GsCombinePieceOk>(GameServerCommand.GS_COMBINE_PIECE_REQUEST,request);byte[] receipt=peer.Last.ToArray();
            Check(result.cardInfoList.Single().cardID==50301&&result.cardInfoList[0].cardLevel==1&&p.CardIds.Count==cards+1,"Encrypted original summon creates the correct initial SS card at level one");
            Check(result.itemInfoList.Single().itemID==205081309&&result.itemInfoList[0].itemCount==-2&&p.Inventory.GetValueOrDefault(205081309u)==0&&p.Gold==gold&&p.CashPoint==cash,"Summon consumes exactly two own fragments without any wallet charge");
            string state=State();await peer.Request<GsCombinePieceOk>(GameServerCommand.GS_COMBINE_PIECE_REQUEST,request,retry:true);
            Check(State()==state&&peer.Last.SequenceEqual(receipt),"Successful summon retry cannot create a second card or consume fragments twice");
            p.Inventory[205081309]=10;await Reject([205081309],ServerErrorCode.ERROR_ALREADY_EXIST,"Owned Pizidao keeps remaining pieces for evolution");
            ulong sequence=result.cardInfoList[0].cardSeq;p.CardIds[(int)sequence-1]=50304;
            await Reject([205081309],ServerErrorCode.ERROR_ALREADY_EXIST,"Evolved Pizidao also prevents duplicate fragment summon");

            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(runtime);var p=runtime.Store.Find(peer.Key.uid)!;
            Check(p.CardIds.SequenceEqual(new[]{50304u})&&p.Inventory[205081309]==10&&p.Inventory[205081276]==2&&p.Inventory[205081209]==2,"Restored collectible and all unrelated NPC fragments survive server restart without replacement");
        }
        File.WriteAllText(Path.Combine(root,"evidence/card-availability-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Card availability: {checks.Count}/{checks.Count}");
    }
}
