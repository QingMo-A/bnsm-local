using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;

internal static class CommerceTest
{
    internal static async Task Run(string root)
    {
        string production=Path.Combine(root,"LocalServer/saves/players.json");byte[]? original=File.Exists(production) ? SHA256.HashData(File.ReadAllBytes(production)) : null;
        string save=Path.Combine(root,"verification/commerce",Guid.NewGuid().ToString("N"));var checks=new List<string>();
        void Assert(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        T Read<T>(byte[] value)=>(T)WireCodec.Deserialize(typeof(T),value)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);var c=server.Catalog;
        async Task<(ushort Command,byte[] Payload)> Send(TcpClient socket,ushort command,object value) {
            await socket.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(value),true));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var frame=(await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!;if(frame.Length>8192)throw new Exception("Oversized commerce frame");
            var response=codec.Decode(frame,true);return (response.Command,response.Payload);
        }
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="commerce-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var account=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true})).Payload);
        var key=new SessionKeyInfo {uid=account.uid,loginSessionKey=account.loginSessionKey};using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);
        var p=server.Store.Find(account.uid)!;
        async Task<byte[]> Request(GameServerCommand command,object value,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;var reply=await Send(game,(ushort)command,value);string expected=command.ToString().Replace("_REQUEST",success ? "_OK":"_FAIL");
            if(((GameServerCommand)reply.Command).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(reply.Payload).message);return reply.Payload;
        }
        async Task<GsBuyCashProductOk> Order(uint id,bool retry=false)=>Read<GsBuyCashProductOk>(await Request(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST,new GsBuyCashProductRequest {keyInfo=key,cashProductId=id},retry:retry));
        async Task<GsCashProductDeliveryOk> Deliver(bool retry=false)=>Read<GsCashProductDeliveryOk>(await Request(GameServerCommand.GS_CASH_PRODUCT_DELIVERY_REQUEST,new GsCashProductDeliveryRequest {keyInfo=key},retry:retry));
        var bases=c.InitialHeroes.ToArray();
        // Includes the Pizidao family restored in the earlier collectible-card repair.
        Assert(bases.Length==116 && bases.All(x=>x.Rank==1 && x.Id==x.EvolveFirstAncestor),"Every drawable evolution chain has one genuine original initial form");
        int poolRank=GachaRules.Load(root).CardRank;
        Assert(c.DrawPool(false,poolRank).All(x=>x.Talent<=BscTable.CARD_TALENT.CARD_TALENT_A) && c.DrawPool(true,poolRank).All(x=>x.Talent>=BscTable.CARD_TALENT.CARD_TALENT_A),"Local gold and diamond pools declare separate talent ranges without mixing evolution forms");
        uint total=0;
        foreach(var sku in c.BuyCashProducts.OrderBy(x=>x.Id)) {
            var order=await Order((uint)sku.Id);uint purchaseSequence=key.sequenceNo;var again=await Order((uint)sku.Id,true);
            Assert(order.tokenId==again.tokenId && p.CashPoint==total,"Original CNY "+sku.CNY+" SKU creates one pending order without early rewards");
            uint other=(uint)c.BuyCashProducts.First(x=>x.Id!=sku.Id).Id;
            await Request(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST,new GsBuyCashProductRequest {keyInfo=key,cashProductId=other},false,true);
            var delivered=await Deliver();total+=(uint)(sku.Value+sku.RewardCash);
            Assert(delivered.deliveryCount==1 && delivered.coinInfo.cashPoint==total,"Original CNY "+sku.CNY+" SKU grants exactly "+(sku.Value+sku.RewardCash)+" diamonds");
            await Deliver(true);Assert(p.CashPoint==total && p.PendingCharge==null,"Duplicate delivery for CNY "+sku.CNY+" does not add currency twice");
            uint deliverySequence=key.sequenceNo;key.sequenceNo=purchaseSequence;
            await Request(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST,new GsBuyCashProductRequest {keyInfo=key,cashProductId=(uint)sku.Id},retry:true);
            key.sequenceNo=deliverySequence;
            Assert(p.PendingCharge==null,"Replaying an already delivered purchase for CNY "+sku.CNY+" cannot recreate the order");
        }
        // The previous request was intentionally not a real new purchase: check another fresh delivery.
        Assert((await Deliver()).deliveryCount==0,"A fresh delivery poll after settlement reports no outstanding order");
        foreach(uint invalid in new uint[] {0,999,100001})await Request(GameServerCommand.GS_BUY_CASH_PRODUCT_REQUEST,new GsBuyCashProductRequest {keyInfo=key,cashProductId=invalid},false);
        Assert(p.PendingCharge==null && p.CashPoint==total,"Unknown SKUs and task-based growth funds cannot invent rewards");
        foreach(var sku in c.CashProducts.Where(x=>x.SubType==BscTable.CASH_PRODUCT_SUB_TYPE.CASH_PRODUCT_SUB_TYPE_DELIVERY)) {
            var old=sku.ItemId.ToDictionary(id=>(uint)id,id=>p.Inventory.GetValueOrDefault((uint)id));
            await Order((uint)sku.Id);var pending=new PlayerStore(save,c).Find(p.Uid)!.PendingCharge!;
            Assert(pending.Items.Count==sku.ItemId.Count && pending.Cash==0,"CNY "+sku.Cost+" item package saves the original reward snapshot");
            var delivery=await Deliver();
            Assert(delivery.itemList.Count==sku.ItemId.Count && sku.ItemId.Select((id,i)=>p.Inventory[(uint)id]==old[(uint)id]+sku.Quantity[i]).All(x=>x) && p.CashPoint==total,"CNY "+sku.Cost+" item package grants original items without treating Price as diamonds");
            await Deliver(true);Assert(sku.ItemId.Select((id,i)=>p.Inventory[(uint)id]==old[(uint)id]+sku.Quantity[i]).All(x=>x),"Item package delivery retransmission cannot duplicate inventory");
        }
        uint vip=p.VipExp;await Order(100002);var itemSku=c.CashProducts.Single(x=>x.Id==100002);uint itemId=(uint)itemSku.ItemId[0];int held=p.Inventory[itemId];p.Inventory[itemId]=int.MaxValue;
        await Request(GameServerCommand.GS_CASH_PRODUCT_DELIVERY_REQUEST,new GsCashProductDeliveryRequest {keyInfo=key},false);
        Assert(p.PendingCharge!=null && p.CashPoint==total && p.VipExp==vip,"Inventory overflow leaves the complete order pending without partial VIP/currency rewards");
        p.Inventory[itemId]=held;await Deliver();Assert(p.PendingCharge==null,"A blocked order can be delivered once capacity is restored");
        p.CashPoint=500000;p.Gold=1000000;int count=p.CardIds.Count(x=>x!=0);
        for(int i=0;i<15;i++) {
            var pull=Read<GsGachaOk>(await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=4}));
            var pool = c.DrawPool(true,poolRank);
            Assert(pull.newCardList.Count + pull.newItemList.Count == 10 && pull.existCardIdList.Count == 0 &&
                pull.newCardList.All(card => pool.Any(b => b.Id == card.cardID)) &&
                (pull.newCardList.Any(card => pool.Any(b => b.Id == card.cardID && b.Talent >= BscTable.CARD_TALENT.CARD_TALENT_S)) ||
                 pull.newItemList.Any(item => pool.Any(b => b.PieceId == item.itemID && b.Talent >= BscTable.CARD_TALENT.CARD_TALENT_S))),
                "Diamond ten-pull " + (i+1) + " has ten visible rewards and an S/SS card or matching fragments");
        }
        int cards=p.CardIds.Count(x=>x!=0);uint cash=p.CashPoint;
        await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=4},retry:true);
        Assert(p.CardIds.Count(x=>x!=0)==cards && p.CashPoint==cash,"Ten-pull retransmission preserves the existing draw receipt");
        var goldPull=Read<GsGachaOk>(await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=2}));
        Assert(goldPull.newCardList.All(x=>c.DrawPool(false,poolRank).Any(b=>b.Id==x.cardID)) && goldPull.existCardIdList.All(id=>c.DrawPool(false,poolRank).Any(b=>b.Id==id)),"Gold pulls use only the declared C/B/A forms of the configured rank");
        int owned=p.CardIds.Count(x=>x!=0);
        Assert(owned>=count && owned<=count+160 && p.CardIds.Where(x=>x!=0).Distinct().Count()==owned,"Duplicate heroes convert to pieces instead of taking another card slot (before="+count+", after="+owned+", cap="+300+")");
        var restored=new PlayerStore(save,c).Find(p.Uid)!;Assert(restored.CardIds.SequenceEqual(p.CardIds) && restored.Inventory.OrderBy(x=>x.Key).SequenceEqual(p.Inventory.OrderBy(x=>x.Key)) && restored.CommerceReceipts.Count>0,"Paid items, drawn copies and payment replay receipts survive reload");
        if(original!=null)Assert(SHA256.HashData(File.ReadAllBytes(production)).SequenceEqual(original),"Commerce verification never changes the production profile");
        File.WriteAllText(Path.Combine(root,"evidence/commerce-test.json"),JsonSerializer.Serialize(new {completedAt=DateTimeOffset.Now,passed=checks.Count,checks,saveDirectory=save,initialHeroCount=bases.Length,goldPoolCount=c.DrawPool(false,poolRank).Length,diamondPoolCount=c.DrawPool(true,poolRank).Length},new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"PASS: {checks.Count} initial-card and native recharge checks.");
    }
}
