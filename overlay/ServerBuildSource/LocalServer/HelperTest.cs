using System.Net.Sockets;
using System.Text.Json;

internal static class HelperTest
{
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/helper",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Assert(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        T Read<T>(byte[] b)=>(T)WireCodec.Deserialize(typeof(T),b)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        async Task<byte[]> Login(LoginServerCommand cmd,object request){
            await login.GetStream().WriteAsync(codec.Encode((ushort)cmd,WireCodec.Serialize(request),true));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));return codec.Decode((await WireCodec.ReadFrame(login.GetStream(),timeout.Token))!,true).Payload;
        }
        await Login(LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="helper-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var started=Read<LsGameStartOk>(await Login(LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true}));
        var key=new SessionKeyInfo {uid=started.uid,loginSessionKey=started.loginSessionKey};
        var player=server.Store.Find(started.uid)!;player.Level=99;player.Exp=(uint)server.Catalog.PlayerStates.Single(x=>x.Level==99).Exp;
        using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",server.GamePort);
        async Task<GsGiveItemToHelperOk?> Gift(uint count,bool success=true,bool retry=false){
            if(!retry)key.sequenceNo++;
            var req=new GsGiveItemToHelperRequest {keyInfo=key,itemCount=count};
            await socket.GetStream().WriteAsync(codec.Encode((ushort)GameServerCommand.GS_GIVE_ITEM_TO_HELPER_REQUEST,WireCodec.Serialize(req),true));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));var reply=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);
            Assert(reply.Command==(ushort)(success?GameServerCommand.GS_GIVE_ITEM_TO_HELPER_OK:GameServerCommand.GS_GIVE_ITEM_TO_HELPER_FAIL),"Encrypted helper response for count "+count+(retry?" retry":""));
            return success?Read<GsGiveItemToHelperOk>(reply.Payload):null;
        }
        var rule=server.Catalog.HelperRewards.Single();uint item=(uint)rule.ItemId;
        Assert(item==218000001&&rule.GetMin==50&&rule.GetMax==100,"Recovered helper gift item and reward bounds");
        Assert(rule.RewardPoolList.Sum(x=>x.PoolProb)==100,"Original pool probabilities sum to 100");
        player.Inventory[item]=200;player.HelperLikeRewardAt=100;
        var query=await Gift(0);Assert(query!.accumulation==0&&query.coinInfo!=null&&query.earnedCoinInfo!=null&&query.changedItemList.Count==0&&player.Inventory[item]==200,"Initialization is read-only and has complete optional money objects");
        var one=await Gift(1);Assert(one!.accumulation==1&&one.rewardPoolId==0&&one.changedItemList.Single().itemCount==-1&&player.Inventory[item]==199,"One like consumes one item and records accumulation");
        byte[] before=File.ReadAllBytes(Path.Combine(save,"players.json"));
        var repeated=await Gift(1,retry:true);Assert(repeated!.accumulation==1&&player.Inventory[item]==199&&before.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(save,"players.json"))),"Identical retransmission does not charge or save twice");
        await Gift(2,false,true);Assert(player.Inventory[item]==199&&player.HelperLikeAccumulation==1,"Reused sequence with changed count rejected atomically");
        await Gift(201,false);await Gift(uint.MaxValue,false);await Gift(1001,false);
        Assert(player.Inventory[item]==199&&player.HelperLikeAccumulation==1,"Insufficient inventory and oversized requests have no effects");
        uint helperId=player.HelperId;player.HelperId=uint.MaxValue;await Gift(1,false);
        Assert(player.Inventory[item]==199,"Missing helper cannot receive gifts");player.HelperId=helperId;
        int gate=server.Catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_HELPER_REQUEST);
        if(gate>1&&gate<100){player.Level=(uint)(gate-1);await Gift(1,false);Assert(player.Inventory[item]==199,"Original helper level gate is enforced");player.Level=99;}
        var batch=await Gift(100);Assert(batch!.accumulation==1&&batch.rewardPoolId!=0&&player.Inventory[item]==99,"Batch crosses saved threshold and carries excess accumulation");
        Assert(batch.changedItemList.First().itemID==item&&batch.changedItemList.First().itemCount==-100,"Consumed likes returned as a negative inventory delta");
        Assert(batch.earnedCardList.Count>0||batch.changedItemList.Any(x=>x.itemCount>0)||batch.earnedCoinInfo.cashPoint>0||batch.earnedCoinInfo.gold>0,"Original reward pool delivers a real reward");
        Assert(WireCodec.Serialize(batch).Length<7000,"Helper reply fits original client receive buffer");
        var reload=new PlayerStore(save,server.Catalog).Find(player.Uid)!;
        Assert(reload.Inventory[item]==99&&reload.HelperLikeAccumulation==1&&reload.HelperLikeRewardAt>=50&&reload.HelperLikeRewardAt<=100,"Inventory accumulation and next threshold survive save reload");
        // Exercise every recovered reward pool through the actual request path.
        var probabilities=rule.RewardPoolList.Select(x=>x.PoolProb).ToArray();
        for(int i=0;i<rule.RewardPoolList.Count;i++){
            for(int j=0;j<probabilities.Length;j++)rule.RewardPoolList[j].PoolProb=i==j?100:0;
            player.Inventory[item]=100;player.HelperLikeAccumulation=0;player.HelperLikeRewardAt=100;
            var reward=await Gift(100);Assert(reward!.rewardPoolId==rule.RewardPoolList[i].PoolId,"Recovered reward pool works: "+reward.rewardPoolId);
        }
        for(int i=0;i<probabilities.Length;i++)rule.RewardPoolList[i].PoolProb=probabilities[i];
        foreach(var pool in rule.RewardPoolList){
            var weights=pool.RewardList.Select(x=>x.Prob).ToArray();
            for(int i=0;i<rule.RewardPoolList.Count;i++)rule.RewardPoolList[i].PoolProb=ReferenceEquals(rule.RewardPoolList[i],pool)?100:0;
            for(int i=0;i<weights.Length;i++){
                for(int j=0;j<weights.Length;j++)pool.RewardList[j].Prob=j==i?100:0;
                var expected=pool.RewardList[i];
                player.Inventory[item]=100;player.HelperLikeAccumulation=0;player.HelperLikeRewardAt=100;
                var reward=await Gift(100);
                bool actual=expected.RewardIdType switch {
                    BscTable.REWARD_TYPE.REWARD_TYPE_ITEM=>reward!.changedItemList.Any(x=>x.itemID==expected.RewardId&&x.itemCount==expected.Quantity),
                    BscTable.REWARD_TYPE.REWARD_TYPE_CASH=>reward!.earnedCoinInfo.cashPoint==expected.Quantity,
                    BscTable.REWARD_TYPE.REWARD_TYPE_CARD=>reward!.earnedCardList.Any(x=>x.cardID==expected.RewardId)||reward!.changedItemList.Any(x=>x.itemID==server.Catalog.Cards.Single(c=>c.Id==expected.RewardId).PieceId&&x.itemCount>0),
                    _=>false
                };
                Assert(actual,"Every recovered reward is granted with original quantity: "+expected.RewardIdType+" / "+expected.RewardId);
            }
            for(int i=0;i<weights.Length;i++)pool.RewardList[i].Prob=weights[i];
        }
        for(int i=0;i<probabilities.Length;i++)rule.RewardPoolList[i].PoolProb=probabilities[i];
        player.Inventory[item]=1000;player.HelperLikeRewardAt=50;player.HelperLikeAccumulation=0;
        var max=await Gift(1000);Assert(player.Inventory[item]==0&&WireCodec.Serialize(max).Length<7000,"Maximum batch grants all rewards within the wire cap");
        await Gift(1,false);Assert(player.Inventory[item]==0,"Exhausted gift stack cannot become negative");
        File.WriteAllText(Path.Combine(root,"evidence/helper-protocol-test.json"),JsonSerializer.Serialize(new {passed=true,count=checks.Count,checks,isolatedSave=save},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"Helper protocol: {checks.Count} checks passed");
    }
}
