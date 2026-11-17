using System.Security.Cryptography;
using System.Text.Json;

/// <summary>寻宝（原客户端叫寻宝，协议是 GS_GACHA_*）掉落的一条资源。</summary>
internal sealed class GachaResource
{
    /// <summary>
    /// ITEM / PIECE / GEM / STAMINA。每个抽奖槽位只发一种奖励。
    /// PIECE 取可用角色的 PieceId；GEM 取原宝石表；STAMINA 发可在背包使用的体力药水。
    /// </summary>
    public string Type { get; set; } = "ITEM";
    public uint ItemId { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Weight { get; set; } = 1;
    public int MaxStep { get; set; } = 3;
    public int MinGrade { get; set; }
    public int MaxGrade { get; set; } = 5;
}

/// <summary>
/// 寻宝规则。原表 <c>GachaSetDataTable</c> 给了分池、花费、免费次数、附赠卡经验，
/// 但"槽位→具体卡"的映射表 <c>GachaDataTable</c> 不在这份客户端数据里，
/// 卡池使用用户确认的本地规则：每条进化链取 <see cref="CardRank"/> 阶形态。
/// 原服逐槽位投放和单卡概率未恢复；槽位 ID 前缀不能证明官方投放星级。
/// </summary>
internal sealed class GachaRules
{
    public int CardRank { get; set; } = 4;
    /// <summary>每个非保底槽位抽到资源而非角色的概率（百分比，本地配置）。</summary>
    public int ResourceChancePercent { get; set; } = 80;
    /// <summary>普通角色与碎片候选权重为 100。S 资质共用较低权重。</summary>
    public int STalentWeight { get; set; } = 25;
    /// <summary>重复卡转碎片时按资质给的数量；键是 CARD_TALENT 去掉前缀后的名字。</summary>
    public Dictionary<string, int> PieceByTalent { get; set; } = new()
    {
        ["C"] = 10, ["B"] = 15, ["A"] = 20, ["S"] = 30, ["SS"] = 40
    };
    public List<GachaResource> Resources { get; set; } = [];
    public List<GachaResource> MaterialGoldResources {get;set;} = MaterialDefaults(false);
    public List<GachaResource> MaterialCashResources {get;set;} = MaterialDefaults(true);
    public List<GachaResource> MaterialVipResources {get;set;} = [new() {Type="INGREDIENT",Min=2,Max=5,Weight=60,MinGrade=4},new() {Type="EMPOWER",Min=1,Max=2,Weight=40,MinGrade=4,MaxStep=6}];
    private static List<GachaResource> MaterialDefaults(bool cash) => [
        new() {Type="INGREDIENT",Min=cash?5:3,Max=cash?12:8,Weight=45,MaxGrade=cash?4:2},
        new() {Type="EMPOWER",Min=1,Max=cash?3:2,Weight=25,MaxStep=cash?6:3},
        new() {Type="ITEM",ItemId=cash?230002001u:210002001u,Min=cash?1:5,Max=cash?3:20,Weight=15},
        new() {Type="GEM",Min=1,Max=1,Weight=15,MaxStep=cash?4:2}];


    public int PieceCount(BscTable.CARD_TALENT talent)
    {
        string key = talent.ToString().Replace("CARD_TALENT_", "");
        return PieceByTalent.TryGetValue(key, out int n) && n > 0 ? n : 20;
    }

    public static GachaRules Load(string root)
    {
        string path = Path.Combine(root, "LocalServer", "data", "gacha.json");
        if (!File.Exists(path)) return new GachaRules { Resources = DefaultResources() };
        try
        {
            var rules = JsonSerializer.Deserialize<GachaRules>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (rules == null) return new GachaRules { Resources = DefaultResources() };
            if (rules.Resources.Count == 0) rules.Resources = DefaultResources();
            return rules;
        }
        catch (Exception)
        {
            // 配置写坏了就退回内置默认值：宁可少掉几种资源，也不能让寻宝整个不可用。
            return new GachaRules { Resources = DefaultResources() };
        }
    }

    /// <summary>内置默认：材料 + 卡经验 + 碎片 + 装备宝石 + 体力药水。</summary>
    public static List<GachaResource> DefaultResources() =>
    [
        new() { Type = "ITEM", ItemId = 260000001, Min = 3, Max = 8, Weight = 26 },
        new() { Type = "ITEM", ItemId = 260000002, Min = 2, Max = 6, Weight = 18 },
        new() { Type = "ITEM", ItemId = 210002001, Min = 20, Max = 60, Weight = 22 },
        new() { Type = "PIECE", Min = 1, Max = 3, Weight = 14 },
        new() { Type = "GEM", Min = 1, Max = 1, Weight = 12 },
        new() { Type = "STAMINA", ItemId = 208002003, Min = 1, Max = 1, Weight = 8 },
    ];
}

internal sealed partial class ProtocolRouter
{
    private GachaRules? gachaRules;
    private GachaRules GachaRuleSet => gachaRules ??= GachaRules.Load(RootDirectory);

    /// <summary>
    /// 抽卡。返回 null 表示这条请求不归这里管。
    /// 规则来源：花费/免费次数/附赠卡经验读 GachaSetDataTable；
    /// 卡池按四星重建；按进化链判断重复并转对应碎片；物品与卡牌共用抽奖槽位。
    /// </summary>
    private Reply? HandleGacha(string name, object? request, LocalPlayer player, SessionKeyInfo session,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (name != "GS_GACHA_REQUEST") return null;
        var draw = (GsGachaRequest)request!;

        string fingerprint = Convert.ToHexString(SHA256.HashData(WireCodec.Serialize(draw)));
        string cacheKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.loginSessionKey + "/" + session.sequenceNo)));
        if (player.GachaReceipts.TryGetValue(cacheKey, out var previous))
            return previous.Fingerprint == fingerprint ? new(previous.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(previous.TypeName,true),Convert.FromBase64String(previous.Payload)),previous.Name)
                : fail("sequence reused for a different draw", ServerErrorCode.ERROR_INVALID_ID);

        var set = AvailableGachaSets.FirstOrDefault(x => x.GachaCategoryIndex == draw.gachaGroupIndex);
        if (set == null) return fail("local gacha category is unavailable", ServerErrorCode.ERROR_INVALID_GACHA_GROUP_INDEX);
        if(catalog.VipFor(player.VipExp).VipLv < set.ActivateVipGrade)
            return fail("VIP level is too low for this draw",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
        if(draw.useFreeCount && draw.useFreeGachaTicket)
            return fail("free count and ticket cannot be combined",ServerErrorCode.ERROR_INVALID_ID);
        uint ticket=(uint)Math.Max(0,set.CostFreeTicketItemId);
        int ticketCount=set.CostFreeTicketItemQuantity;
        if(draw.useFreeGachaTicket && (ticket==0 || ticketCount<=0 || player.Inventory.GetValueOrDefault(ticket)<ticketCount))
            return fail("not enough matching gacha tickets",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        bool material=set.GachaTabType==BscTable.GACHA_TAB_TYPE.GACHA_TAB_TYPE_ITEM;
        bool cash = set.CostItemIdType == BscTable.COST_TYPE.COST_TYPE_CASH;
        uint cost = (uint)Math.Max(0, set.CostItemQuantity);
        var stack = GachaInfo(player, set);
        if (draw.useFreeCount && stack.RemainFreeCount == 0)
            return fail("no free draws remain", ServerErrorCode.ERROR_INVALID_GACHA_GROUP_INDEX);
        if (!draw.useFreeCount && !draw.useFreeGachaTicket && (cash ? player.CashPoint : player.Gold) < cost)
            return fail("not enough local currency", cash ? ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT : ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);

        var pool = DrawPool(cash);
        if (pool.Length == 0)
            return fail("local card pool has no valid entries", ServerErrorCode.ERROR_INVALID_GACHA_GROUP_INDEX);

        int draws = Math.Max(1, set.GachaCount);
        // The ten-draw safety slot is A; S remains a weighted random outcome.
        var guaranteed = pool.Where(x => x.Talent == BscTable.CARD_TALENT.CARD_TALENT_A).ToArray();

        var result = new GsGachaOk();
        var newIds = new List<uint>();
        var rules = GachaRuleSet;
        // Original GsGachaOk carries inventory deltas but no stamina field.
        // Recovery potions are visible, persistent and usable even at full stamina.
        var extraCoin = new PKCoinInfo();
        var ownedHeroes = catalog.Cards.Where(c => player.CardIds.Contains((uint)c.Id))
            .Select(HeroIdentity).ToHashSet();

        for (int i = 0; i < draws; i++)
        {
            bool forceHigh = !material && cash && draws >= 10 && i == draws - 1 && guaranteed.Length > 0;
            if (material || (!forceHigh && RandomNumberGenerator.GetInt32(100) < Math.Clamp(rules.ResourceChancePercent,0,100)))
            {
                var resource = RollResource(material ? MaterialResources((uint)set.GachaCategoryIndex,i) : rules.Resources);
                if (resource == null)
                    return fail("invalid local gacha resource configuration", ServerErrorCode.ERROR_INVALID_ID);
                result.newItemList.Add(resource);
                continue;
            }
            var card = WeightedLoot(forceHigh ? guaranteed : pool, x => TalentWeight(x.Talent));

            // 已在卡包里、或本次十连里刚出过一次，都算重复 —— 后者必须显式判断：
            // 用进化链祖先识别角色，不能直接比较随星级变化的 CardId。
            if (!ownedHeroes.Add(HeroIdentity(card)))
            {
                // Do not echo existCardIdList: the original client displays it as
                // another whole card. One piece delta is the actual slot result.
                if (!catalog.Items.Any(x => x.Id == card.PieceId && x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_PIECE))
                    return fail("original hero fragment item is missing", ServerErrorCode.ERROR_INVALID_CARD);
                result.newItemList.Add(ItemInfo((uint)card.PieceId, rules.PieceCount(card.Talent)));
            }
            else if (player.CardIds.Count(x => x != 0) + newIds.Count >= 300)
            {
                return fail("local card storage is full (300)", ServerErrorCode.ERROR_INVALID_CARD);
            }
            else
            {
                newIds.Add((uint)card.Id);
                result.newCardList.Add(CardInfo((uint)card.Id, (ulong)player.CardIds.Count + (ulong)newIds.Count));
            }
        }

        // Validate the complete inventory/bonus/currency plan before changing anything.
        var plan = new ShopGrant {Cards=newIds};
        foreach(var item in result.newItemList) {
            if((long)plan.Items.GetValueOrDefault(item.itemID)+item.itemCount>int.MaxValue)
                return fail("reward amount overflow",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            plan.Items[item.itemID]=plan.Items.GetValueOrDefault(item.itemID)+item.itemCount;
        }
        if (set.BonusRewardQuantity>0 && IsGachaBonusAllowed(set)) {
            if(!AddShopReward(player,plan,set.BonusRewardIdType,set.BonusRewardId,set.BonusRewardQuantity))
                return fail("invalid original bonus",ServerErrorCode.ERROR_INVALID_ID);
            if(set.BonusRewardIdType==BscTable.REWARD_TYPE.REWARD_TYPE_ITEM)result.bonusItemList.Add(ItemInfo((uint)set.BonusRewardId,set.BonusRewardQuantity));
        }
        var currency=cash?BscTable.MONEY_TYPE.MONEY_TYPE_CASH:BscTable.MONEY_TYPE.MONEY_TYPE_GOLD;
        uint paid=draw.useFreeCount || draw.useFreeGachaTicket?0:cost;
        if(!CanPayAndGrant(player,plan,currency,paid))return fail("inventory or currency capacity reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if(draw.useFreeGachaTicket) {
            player.Inventory[ticket]-=ticketCount;
            result.useItemList.Add(ItemInfo(ticket,-ticketCount));
        }
        ApplyShopGrant(player,plan,currency,paid,new List<PKUserItem>(),new List<PKUserCardInfo>());
        extraCoin.gold=(uint)plan.Gold;extraCoin.cashPoint=(uint)plan.Cash;
        if (draw.useFreeCount) player.Gacha[draw.gachaGroupIndex].FreeUsed++;
        player.Gacha[draw.gachaGroupIndex].Drawn += (uint)draws;
        player.CollectedCardIds.UnionWith(newIds);

        if (!extraCoin.IsZero()) result.bonusCoinInfo = extraCoin;
        result.currentCoinInfo = Coins(player);
        result.gachaStackCount = GachaInfo(player, set);
        var reply = ok(result);
        if(player.GachaReceipts.Count>=256)player.GachaReceipts.Remove(player.GachaReceipts.Keys.First());
        player.GachaReceipts[cacheKey]=new() {Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=typeof(GsGachaOk).FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result))};
        store.Save();
        return reply;
    }

    /// <summary>四星池：每条进化链取 Rank 阶的形态（仅使用同版本可收集且资源完整的角色链）。</summary>
    private BscTable.CardData[] DrawPool(bool cash) => catalog.DrawPool(cash, GachaRuleSet.CardRank);

    private int TalentWeight(BscTable.CARD_TALENT talent) => talent == BscTable.CARD_TALENT.CARD_TALENT_S
        ? Math.Clamp(GachaRuleSet.STalentWeight, 1, 99) : 100;

    private Dictionary<int, BscTable.CARD_TALENT>? gachaFragmentTalents;
    private Dictionary<int, BscTable.CARD_TALENT> GachaFragmentTalents => gachaFragmentTalents ??=
        catalog.InitialHeroes.Where(c => c.PieceId > 0)
            .GroupBy(c => c.PieceId).ToDictionary(g => g.Key, g => g.Max(c => c.Talent));

    private bool IsGachaItemAllowed(BscTable.ItemData item) =>
        item.ItemSubType != BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_PIECE_CARD ||
        (GachaFragmentTalents.TryGetValue(item.Id, out var talent) && talent <= BscTable.CARD_TALENT.CARD_TALENT_S);

    private bool IsGachaBonusAllowed(BscTable.GachaSetData set) => set.BonusRewardIdType switch {
        BscTable.REWARD_TYPE.REWARD_TYPE_ITEM => !catalog.ItemsById.TryGetValue(set.BonusRewardId, out var item) || (IsGachaItemAllowed(item) && GachaFragmentTalents.GetValueOrDefault(item.Id) < BscTable.CARD_TALENT.CARD_TALENT_S),
        BscTable.REWARD_TYPE.REWARD_TYPE_CARD => catalog.Cards.Any(c => c.Id == set.BonusRewardId && LocalCardRegistry.FindExtensionForCard(c.Id) == null && c.Talent < BscTable.CARD_TALENT.CARD_TALENT_S),
        _ => true
    };

    internal static int HeroIdentity(BscTable.CardData card) => card.EvolveFirstAncestor > 0 ? card.EvolveFirstAncestor : card.Id;

    private IEnumerable<GachaResource> MaterialResources(uint group,int index) => group==15 && index==9?GachaRuleSet.MaterialVipResources:
        group>=13?GachaRuleSet.MaterialCashResources:GachaRuleSet.MaterialGoldResources;

    private IEnumerable<BscTable.ItemData> ResourceItems(GachaResource spec)
    {
        var items=catalog.Items.AsEnumerable();
        switch(spec.Type.ToUpperInvariant()) {
            case "GEM":return items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM && x.Step>0 && x.Step<=spec.MaxStep);
            case "PIECE":return items.Where(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_PIECE_CARD && IsGachaItemAllowed(x));
            case "INGREDIENT":return items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT && (int)x.Grade>=spec.MinGrade && (int)x.Grade<=spec.MaxGrade);
            case "EMPOWER":return items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE && x.Rank>0 && x.Rank<=spec.MaxStep && (int)x.Grade>=spec.MinGrade && (int)x.Grade<=spec.MaxGrade);
            case "STAMINA":return items.Where(x=>x.Id==spec.ItemId && x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_STAMINA);
            case "ITEM":return items.Where(x=>x.Id==spec.ItemId && IsGachaItemAllowed(x));
            default:return [];
        }
    }
    private PKUserItem? RollResource(IEnumerable<GachaResource> resources)
    {
        var choices=resources.Where(x=>x.Weight>0 && x.Min>0 && x.Max>=x.Min && x.Max<int.MaxValue)
            .Select(x => (Spec: x, Items: ResourceItems(x).ToArray()))
            .Where(x => x.Items.Length > 0).ToArray();
        if(choices.Length==0)return null;
        var selected = WeightedLoot(choices, x => x.Items.Length == 1 &&
            GachaFragmentTalents.GetValueOrDefault(x.Items[0].Id) == BscTable.CARD_TALENT.CARD_TALENT_S
            ? Math.Max(1, (int)((long)x.Spec.Weight * TalentWeight(BscTable.CARD_TALENT.CARD_TALENT_S) / 100))
            : x.Spec.Weight);
        var spec = selected.Spec;
        var item = WeightedLoot(selected.Items, x => TalentWeight(GachaFragmentTalents.GetValueOrDefault(x.Id)));
        return ItemInfo((uint)item.Id,spec.Min==spec.Max?spec.Min:RandomNumberGenerator.GetInt32(spec.Min,spec.Max+1));
    }
    private GsVipGachaListOk GachaPreview()
    {
        var result=new GsVipGachaListOk {requestTimeStamp=Now};
        var featured=DrawPool(true).OrderByDescending(x=>x.Talent).ThenBy(x=>x.Id).Take(6).Select(x=>(uint)x.Id).ToArray();
        result.weeklyIdList.AddRange(featured.Take(3));result.dailyIdList.AddRange(featured.Skip(3));
        foreach(var set in AvailableGachaSets) {
            var visible=new PKGachaVisibleList {gachaCategoryIndex=(uint)set.GachaCategoryIndex};
            if(set.GachaTabType==BscTable.GACHA_TAB_TYPE.GACHA_TAB_TYPE_ITEM) {
                var specs=(set.GachaCategoryIndex==15?GachaRuleSet.MaterialVipResources:[]).Concat(MaterialResources((uint)set.GachaCategoryIndex,0));
                // Rotate among real candidates rather than filling all preview slots with one material family.
                visible.visibleList.AddRange(specs.SelectMany(x=>ResourceItems(x).Take(2)).Select(x=>(uint)x.Id).Distinct().Take(8));
            } else {
                var cards=DrawPool(set.CostItemIdType==BscTable.COST_TYPE.COST_TYPE_CASH);
                visible.visibleList.AddRange(cards.GroupBy(x=>x.Talent).Select(g=>(uint)g.First().Id).Take(3));
                visible.visibleList.AddRange(GachaRuleSet.Resources.SelectMany(x=>ResourceItems(x).Take(1)).Select(x=>(uint)x.Id).Distinct().Take(3));
            }
            result.gachaVisibleList.Add(visible);
        }
        return result;
    }
}
