using System.Net.Sockets;
using System.Text.Json;

internal static class ProfileTest
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
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated profile fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            var p=runtime.Store.GetOrCreate("local-player");p.Level=99;p.Exp=ProgressionRules.PlayerFloor(runtime.Catalog,99)+5000;p.CashPoint=1000;p.VipExp=(uint)runtime.Catalog.Vip.Single(x=>x.VipLv==20).LvUpNeedVipExp;
            ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/profile",Guid.NewGuid().ToString("N"));var checks=new List<string>();void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;
            string State()=>JsonSerializer.Serialize(p);
            async Task Bad(GameServerCommand command,object body,string label,bool retry=false){string before=State();await peer.Request<FailPacket>(command,body,false,retry);Check(before==State(),label+" changes nothing");}
            Task<GsGetTitleListOk> Titles()=>peer.Request<GsGetTitleListOk>(GameServerCommand.GS_GET_TITLE_LIST_REQUEST,new GsGetTitleListRequest{keyInfo=peer.Key});
            GsSelectTitleRequest Frame(uint id)=>new(){keyInfo=peer.Key,titleId=id};
            Task<GsSelectTitleOk> Select(uint id)=>peer.Request<GsSelectTitleOk>(GameServerCommand.GS_SELECT_TITLE_REQUEST,Frame(id));
            GsChangeNickNameRequest Rename(string n)=>new(){keyInfo=peer.Key,nickName=n};
            GsBuyExpShopRequest Buy(uint slot,uint n)=>new(){keyInfo=peer.Key,SlotId=slot,buyTryCount=n};
            var list=await Titles();Check(list.titleList.Count==1&&list.titleList[0].titleId==0,"Legacy save without profile state gets only the default frame");
            await Bad(GameServerCommand.GS_SELECT_TITLE_REQUEST,Frame(1020),"Unearned V20 frame");await Bad(GameServerCommand.GS_SELECT_TITLE_REQUEST,Frame(999999),"Unknown frame");
            foreach(var v in new[]{(1,1001),(5,1005),(11,1005),(12,1011),(16,1016),(20,1020)}) {
                p.VipExp=(uint)c.Vip.Single(x=>x.VipLv==v.Item1).LvUpNeedVipExp;list=await Titles();
                Check(list.titleList.Any(t=>t.titleId==v.Item2),"Original VIP milestone unlocks at V"+v.Item1);
                if(v.Item1==11)Check(!list.titleList.Any(t=>t.titleId==1011),"VIP12 frame is not incorrectly granted at VIP11");
            }
            var selected=await Select(1020);Check(selected.nameCardInfo.commonInfo.titleId==1020&&p.SelectedTitle==1020,"Selected earned frame appears in native namecard");
            Check((await Select(0)).nameCardInfo.commonInfo.titleId==0,"Default frame can be selected again");
            await Bad(GameServerCommand.GS_SELECT_TITLE_REQUEST,Frame(3012),"Unearned ranking frame");
            uint voucher=(uint)c.Titles.Single(t=>t.Id==3012).ItemId;p.Inventory[voucher]=1;
            Check((await Titles()).titleList.Any(t=>t.titleId==3012),"Held original frame voucher is shown as unlockable");
            await Select(3012);Check(!p.Inventory.ContainsKey(voucher)&&p.SelectedTitle==3012,"First frame voucher selection consumes exactly one voucher");
            string state=State();await peer.Request<GsSelectTitleOk>(GameServerCommand.GS_SELECT_TITLE_REQUEST,Frame(3012),retry:true);Check(state==State(),"Frame selection retry cannot consume twice");
            await Select(3012);Check(!p.Inventory.ContainsKey(voucher),"Already unlocked voucher frame is freely reusable");
            foreach(string n in new[]{"a","123456789","坏<名>","含 空格","🙂🙂","A\nB"})await Bad(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename(n),"Invalid nickname "+JsonSerializer.Serialize(n));
            p.CashPoint=99;await Bad(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename("青竹剑客"),"Rename without original 100 diamonds");p.CashPoint=1000;
            var other=server.Store.GetOrCreate("other");other.Nickname="Another";await Bad(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename("aNoThEr"),"Case-insensitive duplicate local nickname");
            var renamed=await peer.Request<GsChangeNickNameOk>(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename("青竹剑客"));Check(p.Nickname=="青竹剑客"&&p.CashPoint==900&&renamed.newNickName==p.Nickname&&renamed.coinInfo.cashPoint==900,"Rename charges original 100 diamonds and returns new name and wallet");
            state=State();await peer.Request<GsChangeNickNameOk>(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename("青竹剑客"),retry:true);Check(state==State(),"Rename retry does not charge again");
            await Bad(GameServerCommand.GS_CHANGE_NICKNAME_REQUEST,Rename("不同名字"),"Sequence collision cannot rename",retry:true);
            for(int i=0;i<3;i++){var n=await peer.Request<GsGetRandomNickNameOk>(GameServerCommand.GS_GET_RANDOM_NICKNAME_REQUEST,new GsGetRandomNickNameRequest{keyInfo=peer.Key});Check(ProtocolRouter.ValidNickname(n.randomNickname)&&n.randomNickname!=p.Nickname,"Random nickname matches native length and Unicode rules "+i);}
            await Bad(GameServerCommand.GS_GET_EXP_SHOP_BUY_LIST_REQUEST,new GsGetExpShopBuyListRequest{keyInfo=peer.Key},"Experience shop below original level 99");
            p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99)+10000;
            var slots=await peer.Request<GsGetExpShopBuyListOk>(GameServerCommand.GS_GET_EXP_SHOP_BUY_LIST_REQUEST,new GsGetExpShopBuyListRequest{keyInfo=peer.Key});Check(slots.buyDataList.Count==11&&slots.buyDataList.All(s=>s.buyCount==0),"All eleven original experience-shop slots start with fresh daily stock");
            var slot=c.ExpShop.Single(s=>s.Slot==3);var goods=c.Goods.Single(g=>g.GoodsId==slot.GoodsId);uint item=(uint)goods.ProductId,exp=p.Exp,inventory=(uint)p.Inventory.GetValueOrDefault(item),gold=p.Gold,cash=p.CashPoint;
            var bought=await peer.Request<GsBuyExpShopOk>(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,3));Check(bought.pcExp==exp-300&&p.Exp==exp-300&&p.Level==99&&p.Gold==gold&&p.CashPoint==cash&&p.Inventory[item]==inventory+3&&bought.deltaItemList.Single().itemID==item&&bought.deltaItemList.Single().itemCount==3,"Bulk purchase spends only original EXP price and grants exact item delta without reducing level or wallet");
            state=State();await peer.Request<GsBuyExpShopOk>(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,3),retry:true);Check(state==State(),"Purchase retry cannot spend EXP or grant boxes twice");
            foreach(var b in new[]{Buy(3,0),Buy(3,101),Buy(999,1),Buy(3,8)})await Bad(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,b,"Invalid quantity slot or daily stock");
            p.Exp=ProgressionRules.PlayerFloor(c,99)+99;await Bad(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,1),"Cannot spend level-floor EXP");
            p.Exp=ProgressionRules.PlayerFloor(c,99)+1000;p.Inventory[item]=int.MaxValue;await Bad(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,1),"Inventory overflow");p.Inventory[item]=(int)inventory+3;
            p.ExpShopDay--;slots=await peer.Request<GsGetExpShopBuyListOk>(GameServerCommand.GS_GET_EXP_SHOP_BUY_LIST_REQUEST,new GsGetExpShopBuyListRequest{keyInfo=peer.Key});Check(slots.buyDataList.All(s=>s.buyCount==0),"UTC+8 day rollover resets every original slot");
            await peer.Request<GsBuyExpShopOk>(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,10));Check(p.Exp==ProgressionRules.PlayerFloor(c,99)&&p.Level==99&&p.ExpShopPurchases[3]==10,"Exact remaining EXP can be spent without losing level 99");await Bad(GameServerCommand.GS_BUY_EXP_SHOP_REQUEST,Buy(3,1),"Sold out daily slot");
            p.Titles[3012].ExtraFields=new(){["futureFrame"]=JsonSerializer.SerializeToElement(42)};server.Store.Save();
        }
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server,"local-player");var p=server.Store.Find(peer.Key.uid)!;
            Check(p.Nickname=="青竹剑客"&&p.CashPoint==900,"Nickname and rename fee survive server restart");Check(p.SelectedTitle==3012&&p.Titles[3012].ExtraFields!["futureFrame"].GetInt32()==42,"Selected voucher frame and future save fields survive restart");Check(p.Level==99&&p.Exp==ProgressionRules.PlayerFloor(server.Catalog,99)&&p.ExpShopPurchases[3]==10,"Experience floor and daily purchase stock survive restart");
            Check((await peer.Request<GsSelectTitleOk>(GameServerCommand.GS_SELECT_TITLE_REQUEST,new GsSelectTitleRequest{keyInfo=peer.Key,titleId=3012})).nameCardInfo.commonInfo.nick=="青竹剑客","Fresh native namecard returns persisted nickname and selected frame");
        }
        File.WriteAllText(Path.Combine(root,"evidence/profile-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Profile protocol: {checks.Count}/{checks.Count}");
    }
}
