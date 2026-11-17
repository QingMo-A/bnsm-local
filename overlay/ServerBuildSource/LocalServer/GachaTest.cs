using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>Actual encrypted TCP gacha, inventory and character-family regression checks.</summary>
internal static class GachaTest
{
    internal static async Task Run(string root)
    {
        string production = Path.Combine(root, "LocalServer/saves/players.json");
        byte[]? original = File.Exists(production) ? SHA256.HashData(File.ReadAllBytes(production)) : null;
        string save = Path.Combine(root, "verification/gacha", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Assert(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        T Read<T>(byte[] data) => (T)WireCodec.Deserialize(typeof(T), data)!;
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        await using var server = new LocalRuntime(root, save, 0, 0, 0);
        var c = server.Catalog;
        var rules = GachaRules.Load(root);
        void Rules(GachaRules value) => typeof(ProtocolRouter).GetField("gachaRules", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(server.Router, value);
        GachaRules Forced(params GachaResource[] resources) => new() { CardRank = rules.CardRank, ResourceChancePercent = resources.Length == 0 ? 0 : 100, Resources = resources.ToList() };
        async Task<(ushort Command, byte[] Payload)> Send(TcpClient socket, ushort command, object message)
        {
            await socket.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(message), true));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var frame = (await WireCodec.ReadFrame(socket.GetStream(), deadline.Token))!;
            var response = codec.Decode(frame, true); return (response.Command, response.Payload);
        }
        using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
        await Send(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = "gacha-test", userPW = "local", osType = DeviceOSType.DEVICE_PC });
        var account = Read<LsGameStartOk>((await Send(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true })).Payload);
        var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
        using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
        async Task<byte[]> Request(GameServerCommand command, object message, bool success = true)
        {
            key.sequenceNo++;
            var response = await Send(game, (ushort)command, message);
            string expected = command.ToString().Replace("_REQUEST", success ? "_OK" : "_FAIL");
            if (((GameServerCommand)response.Command).ToString() != expected)
                throw new Exception(expected + ": " + Read<FailPacket>(response.Payload).message);
            return response.Payload;
        }
        async Task<GsGachaOk> Pull(uint group, bool free = false) => Read<GsGachaOk>(await Request(GameServerCommand.GS_GACHA_REQUEST,
            new GsGachaRequest { keyInfo = key, gachaGroupIndex = group, useFreeCount = free }));
        int Slots(GsGachaOk result) => result.newCardList.Count + result.existCardIdList.Count + result.newItemList.Count;
        var p = server.Store.Find(account.uid)!;
        p.Gold = 5_000_000; p.CashPoint = 200_000; p.Stamina = c.StaminaMax(p.Level);
        var gold = c.DrawPool(false, rules.CardRank);
        var cash = c.DrawPool(true, rules.CardRank);
        Assert(gold.Length > 0 && cash.Length > 0 && gold.Concat(cash).All(x => x.Rank == rules.CardRank), "Configured four-star card pools are valid");
        Assert(gold.All(x => x.Talent <= BscTable.CARD_TALENT.CARD_TALENT_A) && cash.All(x => x.Talent is BscTable.CARD_TALENT.CARD_TALENT_A or BscTable.CARD_TALENT.CARD_TALENT_S), "Gold and diamond talent ranges are separate and SS is excluded");
        Assert(gold.Concat(cash).All(card => c.Items.Any(x => x.Id == card.PieceId && x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_PIECE)), "Every drawable hero has a real matching fragment item");
        Assert(c.Items.Any(x => x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_GEM && x.Step > 0 && x.Step <= 3), "Original gem table is loaded with drawable gems");
        Assert(rules.Resources.Any(x => x.Type == "GEM") && !rules.Resources.Any(x => x.Type == "CASH"), "Gem rewards are equipment gems rather than diamond currency");
        Assert(rules.Resources.All(x => x.Min > 0 && x.Max >= x.Min && x.Weight > 0), "Configured resource amounts and weights are positive");

        // A card-only control isolates original price, free quota and evolution rules.
        Rules(Forced());
        uint beforeGold = p.Gold;
        var free = await Pull(1, true);
        var goldSet = c.GachaSets.Single(x => x.GachaCategoryIndex == 1);
        Assert(Slots(free) == 1 && p.Gold == beforeGold, "Original free draw grants exactly one visible result without payment");
        Assert(free.bonusItemList.Any(x => x.itemID == goldSet.BonusRewardId && x.itemCount == goldSet.BonusRewardQuantity), "Original table bonus experience is returned separately");
        Assert(p.Inventory.GetValueOrDefault((uint)goldSet.BonusRewardId) >= goldSet.BonusRewardQuantity, "Bonus experience is persisted");
        await Pull(1);
        Assert(p.Gold == beforeGold - goldSet.CostItemQuantity, "Paid gold draw charges original table price");

        // The reported bug: owning rank 1 or an evolved rank 6 must match a rank 4 draw.
        foreach (int rank in new[] { 1, 6 })
        {
            p.CardIds = gold.Select(card => (uint)c.UsableHeroes.Single(x => x.EvolveFirstAncestor == card.EvolveFirstAncestor && x.Rank == rank).Id).ToList();
            int owned = p.CardIds.Count;
            var inventory = new Dictionary<uint, int>(p.Inventory);
            var converted = await Pull(2);
            Assert(converted.newCardList.Count == 0 && converted.existCardIdList.Count == 0 && converted.newItemList.Count == 10, "Owned rank " + rank + " heroes display ten fragment rewards without whole-card echoes");
            Assert(converted.newItemList.All(item => gold.Any(card => card.PieceId == item.itemID && rules.PieceCount(card.Talent) == item.itemCount)), "Rank " + rank + " duplicates return their own hero fragments and configured quantity");
            Assert(p.CardIds.Count == owned, "Rank " + rank + " duplicate conversion does not consume card slots");
            Assert(converted.newItemList.Concat(converted.bonusItemList).GroupBy(x => x.itemID).All(g => p.Inventory.GetValueOrDefault(g.Key) == inventory.GetValueOrDefault(g.Key) + g.Sum(x => x.itemCount)), "Rank " + rank + " duplicate fragments match saved inventory deltas");
        }

        // Ten copies of a previously unowned family: only the first is a new card.
        var onlyFamily = gold[0];
        var excludedCards = gold.Where(x => x.EvolveFirstAncestor != onlyFamily.EvolveFirstAncestor)
            .Select(card => (Card: card, Talent: card.Talent)).ToArray();
        foreach (var row in excludedCards) row.Card.Talent = BscTable.CARD_TALENT.CARD_TALENT_SS;
        p.CardIds = [];
        var repeatedNew = await Pull(2);
        Assert(repeatedNew.newCardList.Count == 1 && repeatedNew.newItemList.Count == 9 && repeatedNew.existCardIdList.Count == 0, "A newly acquired hero repeats within the same ten-pull as nine fragment rewards");
        Assert(p.CardIds.Count == 1 && repeatedNew.newItemList.All(x => x.itemID == onlyFamily.PieceId && x.itemCount == rules.PieceCount(onlyFamily.Talent)), "Same-pull duplicates use one card slot and the correct hero fragments");
        foreach (var row in excludedCards) row.Card.Talent = row.Talent;

        // Force every configured reward branch over TCP; no probabilistic coverage gaps.
        var resourceReports = new List<object>();
        foreach (var resource in rules.Resources)
        {
            Rules(Forced(resource));
            var inventory = new Dictionary<uint, int>(p.Inventory);
            uint stamina = p.Stamina, diamonds = p.CashPoint;
            var result = await Pull(2);
            Assert(Slots(result) == 10 && result.newCardList.Count == 0 && result.existCardIdList.Count == 0, resource.Type + ": ten slots show ten items, with no extra hidden card");
            Assert(result.newItemList.All(item => item.itemCount >= resource.Min && item.itemCount <= resource.Max && c.Items.Any(x => x.Id == item.itemID)), resource.Type + ": valid original items and quantities");
            Assert(result.newItemList.Concat(result.bonusItemList).GroupBy(x => x.itemID).All(g => p.Inventory.GetValueOrDefault(g.Key) == inventory.GetValueOrDefault(g.Key) + g.Sum(x => x.itemCount)), resource.Type + ": response, bonus and inventory agree, including repeated item IDs");
            Assert(p.Stamina == stamina && p.CashPoint == diamonds, resource.Type + ": full stamina does not discard potion rewards or change diamonds");
            if (resource.Type == "GEM") Assert(result.newItemList.All(item => c.Items.Single(x => x.Id == item.itemID).ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_GEM), "Every gem result is a real equipment gem");
            if (resource.Type == "PIECE") Assert(result.newItemList.All(item => c.InitialHeroes.Any(hero =>
                hero.PieceId == item.itemID && hero.Talent <= BscTable.CARD_TALENT.CARD_TALENT_S)), "Random fragment slots only issue C through S hero fragments");
            if (resource.Type == "STAMINA")
            {
                var potion = result.newItemList.First();
                p.Stamina = 20; uint beforeStamina = p.Stamina;
                var used = Read<GsUseRecoveryItemOk>(await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST, new GsUseRecoveryItemRequest { keyInfo = key, itemID = potion.itemID, itemCount = 1, itemUseType = UseRecoveryItemType.RECOVERY_STAMINA }));
                Assert(used.stamina == beforeStamina + c.Items.Single(x => x.Id == potion.itemID).RecoveryValue && used.deltaItemInfo.itemCount == -1, "Drawn stamina potion is usable through the original recovery protocol");
                p.Stamina = c.StaminaMax(p.Level);
            }
            resourceReports.Add(new { resource.Type, resource.ItemId, result.newItemList, result.bonusItemList });
        }

        // Resources occupy ordinary slots; the final diamond slot guarantees A.
        Rules(Forced(new GachaResource() { Type = "ITEM", ItemId = 210002001, Min = 2, Max = 2 }));
        p.CardIds = cash.Select(card => (uint)c.UsableHeroes.Single(x => x.EvolveFirstAncestor == card.EvolveFirstAncestor && x.Rank == 1).Id).ToList();
        uint cashBefore = p.CashPoint;
        var ten = await Pull(4);
        Assert(Slots(ten) == 10 && ten.newItemList.Count == 10 && ten.existCardIdList.Count == 0, "Mixed diamond ten-pull fits the original ten-result UI");
        Assert(ten.newItemList.Count(x => x.itemID == 210002001) == 9, "Only nine ordinary slots are replaced by resources");
        Assert(ten.newItemList.Any(item => cash.Any(card => card.PieceId == item.itemID && card.Talent == BscTable.CARD_TALENT.CARD_TALENT_A)), "Final A guarantee converts to the matching fragments when already owned");
        Assert(p.CashPoint == cashBefore - c.GachaSets.Single(x => x.GachaCategoryIndex == 4).CostItemQuantity, "Diamond price matches original table with no invisible currency reward");

        // Failed full-bag draws must not leak the nine planned resource slots.
        p.CardIds = Enumerable.Repeat((uint)gold.First(x => x.Talent < BscTable.CARD_TALENT.CARD_TALENT_S).Id, 300).ToList();
        var snapshot = JsonSerializer.Serialize(new { p.Inventory, p.CardIds, p.CashPoint, p.Gold, p.Stamina, drawn = p.Gacha[4].Drawn });
        await Request(GameServerCommand.GS_GACHA_REQUEST, new GsGachaRequest { keyInfo = key, gachaGroupIndex = 4 }, false);
        Assert(snapshot == JsonSerializer.Serialize(new { p.Inventory, p.CardIds, p.CashPoint, p.Gold, p.Stamina, drawn = p.Gacha[4].Drawn }), "Full-bag failure is atomic after nine planned resource rewards");
        var fullBagResource = await Pull(2);
        Assert(Slots(fullBagResource) == 10 && p.CardIds.Count == 300, "A full card bag still accepts item-only draws");

        key.sequenceNo++;
        uint replaySequence=key.sequenceNo;
        var replayRequest = new GsGachaRequest { keyInfo = key, gachaGroupIndex = 2 };
        var first = await Send(game, (ushort)GameServerCommand.GS_GACHA_REQUEST, replayRequest);
        // Ambient activity can update the profile between TCP requests.
        string DrawState() => JsonSerializer.Serialize(new
        {
            p.Inventory, p.EquipmentItems, p.CardIds, p.CardStates, p.CollectedCardIds, p.Gacha, p.GachaReceipts,
            p.CashPoint, p.Gold, p.Stamina
        });
        string afterFirst = DrawState();
        var second = await Send(game, (ushort)GameServerCommand.GS_GACHA_REQUEST, replayRequest);
        Assert((GameServerCommand)first.Command == GameServerCommand.GS_GACHA_OK && first.Payload.SequenceEqual(second.Payload), "Same sequence retransmission returns identical reward bytes");
        Assert(afterFirst == DrawState(), "Retransmission cannot duplicate fragments, items or charges");
        var explicitSsPiece = new GachaResource { Type = "ITEM", ItemId = 205081309, Min = 1, Max = 1, Weight = 100 };
        var expFallback = rules.Resources.Single(x => x.ItemId == 210002001);
        Rules(Forced(explicitSsPiece, expFallback));
        var filtered = await Pull(2);
        Assert(filtered.newItemList.All(x => x.itemID == 210002001), "Explicit ITEM selectors cannot reintroduce SS fragments");
        Rules(rules);
        Assert(rules.ResourceChancePercent==80,"Default ordinary-slot hero probability is reduced from 85% to 20%");
        var stocks=Read<GsGetUserGachaInfoOk>(await Request(GameServerCommand.GS_GET_USER_GACHA_INFO_REQUEST,new GsGetUserGachaInfoRequest {keyInfo=key}));
        Assert(stocks.GachaStackCount.Select(x=>x.GachaGroupIndex).Order().SequenceEqual(new uint[]{1,2,3,4,5,11,12,13,14,15}),"All ten original card, material and V9 categories have stock records");
        var previews=Read<GsVipGachaListOk>(await Request(GameServerCommand.GS_VIP_GACHA_LIST_REQUEST,new GsVipGachaListRequest {keyInfo=key}));
        Assert(previews.gachaVisibleList.Count==10 && previews.gachaVisibleList.All(x=>x.visibleList.Count>0),"Both native tabs receive nonempty real reward previews");
        Assert(previews.gachaVisibleList.SelectMany(x=>x.visibleList).All(id=>c.Items.Any(x=>x.Id==id)||c.UsableHeroes.Any(x=>x.Id==id)),"Preview IDs resolve against the same-version card/item tables");
        Assert(previews.dailyIdList.Count+previews.weeklyIdList.Count==6 && previews.dailyIdList.Concat(previews.weeklyIdList).All(id=>cash.Any(x=>x.Id==id)),"V9 preview carries six eligible featured cards without inventing missing schedule tables");
        p.CashPoint=2_000_000;p.Gold=1_000_000_000;
        foreach(uint group in new uint[]{5,15}) {
            uint coins=p.CashPoint;
            await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=group},false);
            Assert(p.CashPoint==coins,"V9 draw "+group+" rejects insufficient VIP without charging");
        }
        foreach(uint group in new uint[]{11,12,13,14}) {
            uint oldGold=p.Gold,oldCash=p.CashPoint;int count=p.CardIds.Count;
            var materials=await Pull(group);var row=c.GachaSets.Single(x=>x.GachaCategoryIndex==group);
            Assert(Slots(materials)==row.GachaCount && materials.newCardList.Count==0 && materials.newItemList.All(x=>c.Items.Single(y=>y.Id==x.itemID).ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_PIECE),"Material category "+group+" grants only visible materials/gems/experience with a full card bag");
            Assert(p.CardIds.Count==count && (row.CostItemIdType==BscTable.COST_TYPE.COST_TYPE_GOLD?p.Gold==oldGold-row.CostItemQuantity:p.CashPoint==oldCash-row.CostItemQuantity) && materials.bonusItemList.Any(x=>x.itemCount==row.BonusRewardQuantity),"Material category "+group+" uses original cost, count and bonus experience");
        }
        // Exercise every material resource selector, including the guaranteed V tier.
        foreach(bool paid in new[]{false,true})foreach(var spec in paid?rules.MaterialCashResources:rules.MaterialGoldResources) {
            var forced=Forced();if(paid)forced.MaterialCashResources=[spec];else forced.MaterialGoldResources=[spec];Rules(forced);
            var branch=await Pull(paid?14u:12u);
            Assert(branch.newItemList.Count==10 && branch.newItemList.All(x=>x.itemCount>=spec.Min && x.itemCount<=spec.Max),"Material "+(paid?"diamond ":"gold ")+spec.Type+" selector returns valid original items and counts");
        }
        p.VipExp=2_000_000;
        foreach(var spec in rules.MaterialVipResources) {
            var forced=Forced();forced.MaterialVipResources=[spec];Rules(forced);
            var branch=await Pull(15);
            Assert(branch.newItemList.Count==10 && (int)c.Items.Single(x=>x.Id==branch.newItemList.Last().itemID).Grade>=4,"V material guaranteed "+spec.Type+" slot grants an original high-grade material");
        }
        p.CardIds=c.InitialHeroes.Select(x=>(uint)x.Id).ToList();Rules(Forced(new GachaResource {Type="ITEM",ItemId=210002001,Min=1,Max=1}));
        var vipDraw=await Pull(5);
        Assert(Slots(vipDraw)==10 && vipDraw.newItemList.Any(x=>cash.Any(card=>card.Talent==BscTable.CARD_TALENT.CARD_TALENT_A && card.PieceId==x.itemID && x.itemCount==rules.PieceCount(card.Talent))),"V9 ten-pull guarantees a four-star A hero or its matching duplicate fragments");
        Rules(rules);
        foreach(uint group in new uint[]{1,2,3,4,5}) {
            var row=c.GachaSets.Single(x=>x.GachaCategoryIndex==group);uint ticket=(uint)row.CostFreeTicketItemId;
            p.Inventory[ticket]=2;uint oldGold=p.Gold,oldCash=p.CashPoint;
            var ticketDraw=Read<GsGachaOk>(await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=group,useFreeGachaTicket=true}));
            Assert(p.Inventory[ticket]==1 && p.Gold==oldGold && p.CashPoint==oldCash && ticketDraw.useItemList.Any(x=>x.itemID==ticket && x.itemCount==-1),"Original ticket for category "+group+" is consumed instead of currency and returned as a client delta");
        }
        var guard=JsonSerializer.Serialize(p);
        await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=11,useFreeGachaTicket=true},false);
        Assert(guard==JsonSerializer.Serialize(p),"A pool without a matching ticket cannot silently accept an unrelated ticket");
        await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=1,useFreeGachaTicket=true,useFreeCount=true},false);
        Assert(guard==JsonSerializer.Serialize(p),"Combining free quota and ticket is rejected atomically");
        int exp=p.Inventory.GetValueOrDefault(210002001u);p.Inventory[210002001]=int.MaxValue;guard=JsonSerializer.Serialize(p);
        await Request(GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=12},false);
        Assert(guard==JsonSerializer.Serialize(p),"Overflowing bonus inventory rejects the entire material pull before payment or rewards");p.Inventory[210002001]=exp;
        // Observe the real encrypted protocol with an unambiguous resource control.
        var measured=Forced(new GachaResource {Type="ITEM",ItemId=210002001,Min=1,Max=1});measured.ResourceChancePercent=80;Rules(measured);
        int heroSlots=0;
        for(int i=0;i<100;i++) {var sample=await Pull(2);heroSlots+=sample.newCardList.Count+sample.newItemList.Count(x=>x.itemID!=210002001);}
        Assert(heroSlots>100 && heroSlots<350,"1000 non-guaranteed slots have a materially lower hero rate (observed "+heroSlots+"/1000; configured 20%)");
        Rules(rules);
        uint latestSequence=key.sequenceNo;key.sequenceNo=replaySequence;
        string beforeOldRetry=DrawState();
        var oldRetry=await Send(game,(ushort)GameServerCommand.GS_GACHA_REQUEST,replayRequest);
        Assert(oldRetry.Payload.SequenceEqual(first.Payload) && DrawState()==beforeOldRetry,"An older successful sequence remains idempotent after later draws");
        var changedRetry=await Send(game,(ushort)GameServerCommand.GS_GACHA_REQUEST,new GsGachaRequest {keyInfo=key,gachaGroupIndex=3});
        Assert((GameServerCommand)changedRetry.Command==GameServerCommand.GS_GACHA_FAIL && DrawState()==beforeOldRetry,"Reusing an old successful sequence with changed draw parameters is rejected atomically");
        key.sequenceNo=latestSequence;
        var reloaded = new PlayerStore(save, c).Find(p.Uid)!;
        Assert(reloaded.GachaReceipts.Count>0,"Successful gacha receipts survive profile reload");
        Assert(reloaded.Inventory.OrderBy(x => x.Key).SequenceEqual(p.Inventory.OrderBy(x => x.Key)) && reloaded.CardIds.SequenceEqual(p.CardIds), "All reward categories and owned cards survive a server reload");
        byte[]? after = File.Exists(production) ? SHA256.HashData(File.ReadAllBytes(production)) : null;
        Assert(original == null ? after == null : original.SequenceEqual(after!), "Verification never changes the real player save");
        File.WriteAllText(Path.Combine(root, "evidence/gacha-mixed-rewards-test.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, resources = resourceReports, saveDirectory = save, productionSaveUsed = false }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine("PASS: " + checks.Count + " mixed gacha and cross-rank duplicate checks.");
    }
}
