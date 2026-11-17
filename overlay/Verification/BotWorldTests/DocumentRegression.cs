using System.Text.Json;
using System.Reflection;

internal static class DocumentRegression
{
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");Directory.CreateDirectory(folder);
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();
        p.EvilWar=new(){Stage=49,BestStage=48,Slots=[5000,5000,5000,5000,5000],EvilStone=1e25,HolyStone=10000};
        p.Mailbox.Pending.Add(new(){Sequence=1,Time=ProtocolRouter.Now,Kind=22,Rank=10000,Message="Native mailbox verification",Rewards=[new(){Type=(uint)BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT,Count=5000},new(){Type=(uint)BscTable.REWARD_TYPE.REWARD_TYPE_CASH,Count=50},new(){Type=(uint)BscTable.REWARD_TYPE.REWARD_TYPE_CARD,Id=(int)p.CardIds[0],Count=1}]});p.Mailbox.NextSequence=2;runtime.Store.Save();
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));await Task.Delay(TimeSpan.FromMinutes(30));
    }
    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/document-server-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidDataException(label);checks.Add(label);System.Console.WriteLine("PASS "+label);}
        // Use a future native deadline: the live runtime timer must not settle
        // this synthetic ledger while the test exercises its explicit clock.
        ulong cutoff=ProtocolRouter.ArenaRewardCutoff(ProtocolRouter.Now)+86400;string account="local-player";
        uint expectedArena=0,expectedCash=0;ulong claimed=0;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();p.Competitive.Records[12]=new(){Battles=1,Rank=10000,BestRank=10000,Latest=cutoff-60};
            p.Mailbox.ArenaClockInitialized=true;p.Mailbox.ArenaCutoff=cutoff-86400;p.Mailbox.ArenaRanks.Add(new(){Time=cutoff-60,Rank=10000});runtime.Store.Save();
            Check(ProtocolRouter.ArenaRewardCutoff(cutoff-1)==cutoff-86400&&ProtocolRouter.ArenaRewardCutoff(cutoff)==cutoff,"UTC+8 daily cutoff changes exactly at 22:00");
            runtime.Router.SettlementTick(cutoff-1);Check(p.Mailbox.Pending.Count==0,"No payout before deadline");
            runtime.Router.SettlementTick(cutoff);Check(p.Mailbox.Pending.Count==1&&p.Mailbox.Pending[0].Rank==10000,"Deadline queues original rank reward in mailbox");
            var mail=p.Mailbox.Pending.Single();claimed=mail.Sequence;expectedArena=(uint)mail.Rewards.Where(x=>x.Type==(uint)BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT).Sum(x=>x.Count);expectedCash=(uint)mail.Rewards.Where(x=>x.Type==(uint)BscTable.REWARD_TYPE.REWARD_TYPE_CASH).Sum(x=>x.Count);
            Check(expectedArena>0&&expectedCash>0,"Original arena table grants both diamonds and arena currency");
            string state=JsonSerializer.Serialize(p.Mailbox);runtime.Router.SettlementTick(cutoff);runtime.Router.SettlementTick(cutoff-86400);Check(JsonSerializer.Serialize(p.Mailbox)==state,"Repeated tick and clock rollback cannot duplicate payout");
            using var peer=new Peer(root);await peer.Login(runtime,account);
            var list=await peer.Request<GsGetRewardInfoOk>(GameServerCommand.GS_GET_REWARD_LIST_REQUEST,new GsGetRewardInfoRequest());Check(list.rewardList.Count==1&&list.rewardList[0].rewardItemInfo.Count==mail.Rewards.Count,"Native encrypted mail list exposes real attachments");
            p.CashPoint=uint.MaxValue;runtime.Store.Save();uint arena=p.ArenaPoint;
            var request=new GsReceiveRewardItemDataRequest{rewardSeq=claimed};
            var fail=await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,request,success:false);
            Check(fail.errorCode==(uint)ServerErrorCode.ERROR_OVER_MAX_COUNT&&p.CashPoint==uint.MaxValue&&p.ArenaPoint==arena&&p.Mailbox.Pending.Count==1,"Attachment capacity failure preserves mail and every balance");
            p.CashPoint=1000;runtime.Store.Save();uint cash=p.CashPoint;
            var receive=await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,request);
            Check(p.Mailbox.Pending.Count==0&&p.CashPoint==cash+expectedCash&&p.ArenaPoint==arena+expectedArena&&receive.rewardData.Count==1,"Claim credits native currencies exactly once");
            state=JsonSerializer.Serialize(new{p.CashPoint,p.ArenaPoint,p.Mailbox});
            var retry=await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,request,retry:true);Check(JsonSerializer.Serialize(new{p.CashPoint,p.ArenaPoint,p.Mailbox})==state&&retry.rewardData[0].rewardSeq==claimed,"Same packet retry returns receipt without a second grant");
            await peer.Request<FailPacket>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,request,success:false);Check(p.Mailbox.Pending.Count==0&&p.CashPoint==cash+expectedCash,"A fresh duplicate claim cannot grant again");
            var shopRequest=new GsGetIngameShopSellingListRequest();shopRequest.shopId.Add(4);
            var shop=await peer.Request<GsGetIngameShopSellingListOk>(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,shopRequest);
            Check(shop.shopSetDataList.Count==1&&shop.shopSetDataList[0].goodsList.Count>0&&shop.shopSetDataList[0].goodsList.All(x=>x.moneytype==BscTable.MONEY_TYPE.MONEY_TYPE_ARENA_POINT),"Arena shop has original legal offers priced in arena points");
            var slot=p.Markets[4].Slots.FindIndex(x=>x.Vip==0);var offer=shop.shopSetDataList[0].goodsList[slot];var goods=runtime.Catalog.Goods.Single(x=>x.GoodsId==offer.packageId);
            var purchase=new LocalShopBuyRequest{shopId=4,packageId=offer.packageId,SlotIndex=(uint)slot};p.ArenaPoint=0;runtime.Store.Save();
            fail=await peer.Request<FailPacket>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase,success:false);Check(fail.errorCode==(uint)ServerErrorCode.ERROR_NOT_ENOUGH_ARENA_POINT,"Arena shop reports the correct missing currency");
            uint cost=(uint)((ulong)goods.ArenaP*(uint)(100-p.Markets[4].Slots[slot].Discount)/100);
            p.ArenaPoint=checked(cost+100000u);arena=p.ArenaPoint;cash=p.CashPoint;uint gold=p.Gold;runtime.Store.Save();
            var buy=await peer.Request<GsBuyIngameShopGoodsOk>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase);
            Check(p.ArenaPoint==arena-cost&&p.CashPoint==cash&&p.Gold==gold&&buy.deltaItemList.Count>0,"Arena purchase grants goods and charges only its original arena price");
            state=JsonSerializer.Serialize(new{p.ArenaPoint,p.Inventory,p.EquipmentItems});await peer.Request<GsBuyIngameShopGoodsOk>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase,retry:true);Check(JsonSerializer.Serialize(new{p.ArenaPoint,p.Inventory,p.EquipmentItems})==state,"Arena purchase retry is idempotent");
            var previous=p.Markets[4].Slots.Select(x=>runtime.Catalog.Goods.Single(g=>g.GoodsId==x.GoodsId).ProductId).ToHashSet();
            p.Markets[4].NextRefresh=ProtocolRouter.Now-1;
            var refreshed=(await peer.Request<GsGetIngameShopSellingListOk>(GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST,shopRequest)).shopSetDataList.Single();
            var products=refreshed.goodsList.Select(x=>runtime.Catalog.Goods.Single(g=>g.GoodsId==x.packageId).ProductId).ToArray();
            Check(products.Distinct().Count()==products.Length&&!products.Any(previous.Contains)&&refreshed.goodsList.All(x=>x.curStock==0),"Scheduled arena refresh rotates every product and resets stock");
            fail=await peer.Request<FailPacket>(GameServerCommand.GS_BUY_INGAME_SHOP_GOODS_REQUEST,purchase,success:false);
            Check(fail.errorCode==(uint)ServerErrorCode.ERROR_RECOMMEND_SHOP_RESET,"A stale arena-shop purchase asks for the new list without charging");
            p.Mailbox.ArenaRanks.Add(new(){Time=cutoff+1800,Rank=1});runtime.Router.SettlementTick(cutoff+2*86400);
            Check(p.Mailbox.Pending.Count==2&&p.Mailbox.Pending.All(x=>x.Rank==1),"Offline catch-up uses the persisted rank at each deadline");
            runtime.Store.Save();
        }
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)){
            var p=runtime.Store.All().Single();string state=JsonSerializer.Serialize(p.Mailbox);runtime.Router.SettlementTick(cutoff+2*86400);Check(JsonSerializer.Serialize(p.Mailbox)==state,"Restart retains unclaimed mail and settled deadlines");
            using var peer=new Peer(root);await peer.Login(runtime,account);
            var all=await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,new GsReceiveRewardItemDataRequest());Check(all.rewardData.Count==2&&p.Mailbox.Pending.Count==0,"Native claim-all removes every credited mail after restart");
            var infinite=new LocalPvpRecord{Battles=1,Rank=678,BestRank=678,Latest=cutoff+2*86400+120};
            p.Competitive.Records[31]=infinite;
            p.Mailbox.InfiniteArenaCutoff=cutoff+2*86400;
            p.Mailbox.InfiniteArenaRanks.Add(new(){Time=infinite.Latest,Rank=infinite.Rank});
            runtime.Router.SettlementTick(cutoff+3*86400-1);
            Check(p.Mailbox.Pending.Count==0,"Infinite arena waits for its own 22:00 ranking deadline");
            runtime.Router.SettlementTick(cutoff+3*86400);
            runtime.Router.SettlementTick(cutoff+3*86400);
            var boards=p.Mailbox.Pending.Where(x=>x.Kind==(uint)RewardGiveType.REWARD_GIVE_TYPE_INFINITE_ARENA_BATTLE_RANK).ToList();
            Check(boards.Count==1&&boards[0].Rank==678&&boards[0].Rewards.Any(x=>x.Type==(uint)BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT),"Infinite arena queues one native ranking mail with original prizes");
            var rankedPrize=await peer.Request<GsReceiveRewardItemDataOk>(GameServerCommand.GS_RECEIVE_REWARD_REQUEST,new GsReceiveRewardItemDataRequest{rewardSeq=boards[0].Sequence});
            Check(rankedPrize.rewardData.Single().addArenaPoint>0,"Infinite arena prizes can be received by the native mailbox command");
            var rules=new AttendanceRules(root);Check(!rules.ResourceAllOpen(ProtocolRouter.Now),"Legacy perpetual resource activity no longer forces every stage open");
            Check(rules.Operations.Any(x=>!AttendanceRules.EffectiveOperation(x)),"Seed all-open exclusion is explicit without modifying source catalogs");
            p.Resources.Cleared.Add(34);runtime.Store.Save();
            var info=await peer.Request<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest());
            Check(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(info.clearDifficultyResourceStage.AsSpan(6,2))==4&&System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(info.clearDifficultyResourceStage.AsSpan(2,2))==-1,"Login reconstructs resource highest clear and uncleared sentinels");
            var refresh=await peer.Request<LocalResourceAvailabilityReply>(GameServerCommand.GS_RESOURCE_STAGE_GET_USE_COUNT_REQUEST,new GsResourceStageGetUseCountRequest());Check(refresh.clearDifficulty.SequenceEqual(info.clearDifficultyResourceStage),"Resource refresh synchronizes the same sweep unlock array");
            var liveRules=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.Router)!;var open=liveRules.Operations.First(x=>x.Type=="RESOURCE_STAGE_ALL_OPEN");
            open.Start="2026-11-01 00:00:00";open.End="2026-11-07 23:59:59";p.Stamina=10000;p.Resources.Used[3]=0;runtime.Store.Save();
            var sweep=await peer.Request<GsResourceStageSkipOk>(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,new GsResourceStageSkipRequest{stageId=3,difficulty=0,skipCount=1});Check(sweep.earnedList.Count==1,"Clearing a higher resource difficulty unlocks lower-difficulty sweeps as the native UI promises");
            // Reproduce old debug/import saves above the obsolete 200-item check.
            uint equipment=p.EquipmentItems.First().ItemId;
            while(p.EquipmentItems.Count<201)p.EquipmentItems.Add(new(){ItemId=equipment,Sequence=p.NextEquipmentSequence++});
            while(p.CardIds.Count<301)p.CardIds.Add(p.CardIds[0]);
            p.Inventory[205080063]=1;runtime.Store.Save();
            var opened=await peer.Request<GsOpenInventoryBoxItemOk>(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,new GsOpenInventoryBoxItemRequest{boxItemId=205080063,openCount=1});
            Check(opened.deltaItemInfoList.Any(x=>x.itemCount>0)&&!p.Inventory.ContainsKey(205080063),"Gem box opens with existing over-limit gear/cards without a false reset error");
            var resident=runtime.Router.Bots.All().First();var world=typeof(LocalBotWorld).GetField("world",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.Router.Bots)!;
            var events=(List<BotLifeEvent>)world.GetType().GetProperty("Life")!.GetValue(world)!;events.Add(new(){Time=ProtocolRouter.Now,Actor=resident.Uid,Name=resident.Profile.Nickname,Kind="draw",CardId=resident.Profile.CardIds[0]});
            var notice=await peer.Request<GsCheckGameEntryOk>(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest());
            Check(notice.userPollingData.userMessageList.Count==1&&notice.userPollingData.userMessageList[0].idValue==resident.Profile.CardIds[0],"Native lobby polling carries actual resident draw card IDs");
            notice=await peer.Request<GsCheckGameEntryOk>(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST,new GsCheckGameEntryRequest());Check(notice.userPollingData.userMessageList.Count==0,"Polling does not replay the same draw notification");
        }
        string core=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/document-server-latest.json",JsonSerializer.Serialize(new{passed=true,folder,serverCoreSha256=core,checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
