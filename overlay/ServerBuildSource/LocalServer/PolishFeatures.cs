using System.Security.Cryptography;
using ProtoBuf;

internal sealed class LocalFormationLoadout
{
    public List<int> Offense {get;set;}=[0,0,0];
    public List<int> Defense {get;set;}=[0,0,0];
}

// Optional local extension identifies the selected copy; the original APK only sent its template ID.
[ProtoContract]
internal sealed class LocalCardResetRequest
{
    [ProtoMember(1,IsRequired=true)] public SessionKeyInfo keyInfo=null!;
    [ProtoMember(2,IsRequired=true)] public uint cardId;
    [ProtoMember(3)] public ulong cardSeq;
}

internal sealed partial class ProtocolRouter
{
    internal static readonly HashSet<BscTable.GAME_MODE> FormationModes=[BscTable.GAME_MODE.MODE_RANKING_PVP,BscTable.GAME_MODE.MODE_ARENA_PVP,BscTable.GAME_MODE.MODE_CHAINBATTLE_PVP,BscTable.GAME_MODE.MODE_GUILD_ARENA_PVP,BscTable.GAME_MODE.MODE_INFINITE_ARENA_PVP,BscTable.GAME_MODE.MODE_FRIEND_PVP,BscTable.GAME_MODE.MODE_WORLD_TOURNAMENT,BscTable.GAME_MODE.MODE_BALANCE_PVP,BscTable.GAME_MODE.MODE_FACTION_PVP,BscTable.GAME_MODE.MODE_PVP_SERVANT,BscTable.GAME_MODE.MODE_BATTLE_BOARD,BscTable.GAME_MODE.MODE_WORLDWAR];
    private Reply? HandlePolish(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_CARD_RESET_REQUEST" or "GS_GET_FORMATION_LIST_REQUEST" or "GS_ACTIVE_FORMATION_REQUEST" or "GS_ENCHANT_FORMATION_REQUEST" or "GS_USING_FORMATION_REQUEST" or "GS_GET_USING_FORMATION_REQUEST" or "GS_GET_CLASS_RELIC_INFO_REQUEST" or "GS_CLASS_RELIC_ENCHANT_REQUEST"))return null;
        if(name=="GS_GET_FORMATION_LIST_REQUEST") {var r=new GsGetFormationListOk();r.formationInfoList.AddRange(p.FormationLevels.OrderBy(x=>x.Key).Select(x=>new PKFormationInfo{id=x.Key,level=x.Value}));return ok(r);}
        if(name=="GS_GET_USING_FORMATION_REQUEST") {var r=new GsGetUsingFormationOk();foreach(var x in p.FormationLoadouts.OrderBy(x=>x.Key)){var info=new PKFormationUsingInfo{gameMode=x.Key};info.formationId.AddRange(x.Value.Offense);info.defenceFormationId.AddRange(x.Value.Defense);r.formationUsingInfoList.Add(info);}return ok(r);}
        if(name=="GS_GET_CLASS_RELIC_INFO_REQUEST") {var r=new GsGetClassRelicInfoOk{relicPoint=p.Moonstone};r.classRelicLevelVec.AddRange(p.ClassRelicLevels);return ok(r);}
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.PolishReceipts.TryGetValue(key,out var receipt))return receipt.Fingerprint==fingerprint?new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name):fail("polish sequence reused",ServerErrorCode.ERROR_INVALID_ID);
        var gate=name=="GS_CARD_RESET_REQUEST"?BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_RESET:name=="GS_CLASS_RELIC_ENCHANT_REQUEST"?BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_RELIC_ENCHANT:BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_FORMATION;
        if(!FeatureOpen(p,gate))return fail("original player level requirement",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        Reply result;
        if(name=="GS_CARD_RESET_REQUEST")result=ResetCard((LocalCardResetRequest)request!,p,ok,fail);
        else if(name=="GS_CLASS_RELIC_ENCHANT_REQUEST")result=EnchantRelic((GsClassRelicEnchantRequest)request!,p,ok,fail);
        else result=ChangeFormation(name,request!,p,ok,fail);
        if(result.Name.EndsWith("_OK",StringComparison.Ordinal)) {
            if(p.PolishReceipts.Count>=256)p.PolishReceipts.Remove(p.PolishReceipts.Keys.First());
            p.PolishReceipts[key]=new(){Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};store.Save();
        }
        return result;
    }
    private Reply ChangeFormation(string name,object request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_USING_FORMATION_REQUEST") {
            var info=((GsUsingFormationRequest)request).formationUsingInfo;
            if(info==null||!FormationModes.Contains(info.gameMode)||info.formationId.Count!=3||info.defenceFormationId.Count!=3)return fail("original three-deck PvP formation layout required",ServerErrorCode.ERROR_INVALID_FORMATION_SIZE);
            if(info.formationId.Concat(info.defenceFormationId).Any(x=>x!=0&&!p.FormationLevels.ContainsKey(x)))return fail("formation is not activated",ServerErrorCode.ERROR_INVALID_FORMATION_ID);
            p.FormationLoadouts[info.gameMode]=new(){Offense=info.formationId.ToList(),Defense=info.defenceFormationId.ToList()};
            var r=new GsUsingFormationOk();PKFormationInfo Convert(int id)=>new(){id=id,level=p.FormationLevels.GetValueOrDefault(id)};
            r.formationInfoList.AddRange(info.formationId.Select(Convert));r.defenceformationInfoList.AddRange(info.defenceFormationId.Select(Convert));return ok(r);
        }
        int id=(int)Field(request,"formationId")!;var row=catalog.Formations.FirstOrDefault(x=>x.Id==id);
        if(row==null)return fail("invalid original formation",ServerErrorCode.ERROR_INVALID_FORMATION_ID);
        int current=p.FormationLevels.GetValueOrDefault(id);bool activate=name=="GS_ACTIVE_FORMATION_REQUEST";
        if(activate&&current!=0)return fail("already activated",ServerErrorCode.ERROR_ALREADY_EXIST_FORMATION_ID);
        if(!activate&&(current<1||current>=catalog.FormationExpenses.Max(x=>x.TryTime)))return fail("formation is absent or at maximum level",ServerErrorCode.ERROR_NOT_FOUND_FORMATION_EXPENSE);
        var needs=new Dictionary<uint,int>();long gold=0,cash=0;
        if(activate) {
            if(row.ActivateCostValue<0)return fail("invalid activation cost",ServerErrorCode.ERROR_INVALID_FORMATION_ID);
            if(row.ActivateCostType==BscTable.MONEY_TYPE.MONEY_TYPE_GOLD)gold=row.ActivateCostValue;
            else if(row.ActivateCostType==BscTable.MONEY_TYPE.MONEY_TYPE_CASH)cash=row.ActivateCostValue;
            else return fail("unsupported original formation currency",ServerErrorCode.ERROR_INVALID_FORMATION_ID);
        } else {
            var expense=catalog.FormationExpenses.FirstOrDefault(x=>x.TryTime==current);
            if(expense==null||expense.Gold<0||expense.Cash<0||expense.Worldbosspoint!=0||expense.ArenaPoint!=0||expense.RequireItemId.Count!=expense.Quantity.Count)return fail("invalid original expense row",ServerErrorCode.ERROR_NOT_FOUND_FORMATION_EXPENSE);
            gold=expense.Gold;cash=expense.Cash;
            for(int i=0;i<expense.RequireItemId.Count;i++)if(expense.Quantity[i]>0)AddMaterial(needs,(uint)expense.RequireItemId[i],expense.Quantity[i]);
        }
        if(gold>p.Gold)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        if(cash>p.CashPoint)return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        if(needs.Any(x=>p.Inventory.GetValueOrDefault(x.Key)<x.Value))return fail("not enough formation materials",ServerErrorCode.ERROR_DO_NOT_HAVE_ENOUGH_CONSUMABLE_ITEM);
        p.Gold-=(uint)gold;p.CashPoint-=(uint)cash;p.FormationLevels[id]=activate?1:current+1;
        var delta=new List<PKUserItem>();foreach(var x in needs){p.Inventory[x.Key]-=x.Value;delta.Add(ItemInfo(x.Key,-x.Value));}
        if(activate)return ok(new GsActiveFormationOk{formationId=id,coinInfo=Coins(p)});
        var upgraded=new GsEnchantFormationOk{formationId=id,formationLevel=current+1,coinInfo=Coins(p)};upgraded.deltaItemList.AddRange(delta);return ok(upgraded);
    }
    private Reply EnchantRelic(GsClassRelicEnchantRequest req,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_RELIC_ENCHANT_REQUEST);
        var data=catalog.Relics.FirstOrDefault(x=>(uint)x.Class==req.enchantClass);
        if(data==null||req.enchantClass<1||req.enchantClass>=p.ClassRelicLevels.Count)return Invalid("invalid class");
        uint level=p.ClassRelicLevels[(int)req.enchantClass];var next=data.InfoList.FirstOrDefault(x=>x.Level==level+1);
        if(next==null||next.Cost<0||next.NeedExp<0||next.NeedRelicPoint<0||next.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_GOLD)return Invalid("class relic is at maximum level or data is invalid");
        if(level==0&&req.consumeItemVec.Count!=0)return Invalid("activation does not consume fragments");
        if(req.consumeItemVec.Count>catalog.Items.Count)return Invalid("too many materials");
        var needs=new Dictionary<uint,int>();long exp=0;
        foreach(var item in req.consumeItemVec) {
            var row=catalog.Items.FirstOrDefault(x=>x.Id==item.itemID);
            if(row==null||item.itemCount<=0||item.itemSeq!=0||needs.ContainsKey(item.itemID)||row.GetExp<=0)return Invalid("invalid or duplicate material");
            var piece=catalog.Pieces.FirstOrDefault(x=>x.ItemId==item.itemID);
            bool matches=piece!=null&&catalog.Cards.Any(x=>x.Id==piece.NextId&&(uint)x.Class==req.enchantClass);
            if(row.ItemSubType!=BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_ALMIGHTY&&!(row.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE&&matches))return Invalid("fragment belongs to another class");
            if(p.Inventory.GetValueOrDefault(item.itemID)<item.itemCount)return fail("not enough fragments",ServerErrorCode.ERROR_DO_NOT_HAVE_ENOUGH_CONSUMABLE_ITEM);
            needs[item.itemID]=item.itemCount;exp+=(long)row.GetExp*item.itemCount;
        }
        if(exp<next.NeedExp)return Invalid("not enough fragment experience");
        if(p.Gold<next.Cost)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        if(p.Moonstone<next.NeedRelicPoint)return Invalid("not enough moonstones");
        p.Gold-=(uint)next.Cost;p.Moonstone-=(uint)next.NeedRelicPoint;p.ClassRelicLevels[(int)req.enchantClass]=level+1;
        var r=new GsClassRelicEnchantOk{relicPoint=p.Moonstone,nowCoinInfo=Coins(p)};r.classRelicLevelVec.AddRange(p.ClassRelicLevels);
        foreach(var x in needs){p.Inventory[x.Key]-=x.Value;r.deltaItemVec.Add(ItemInfo(x.Key,-x.Value));}return ok(r);
    }
    private Reply ResetCard(LocalCardResetRequest req,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_CARD_RESET_DATA);
        ulong seq=req.cardSeq;
        if(seq==0){var matches=p.CardIds.Select((id,i)=>(id,seq:(ulong)i+1)).Where(x=>x.id==req.cardId).ToList();if(matches.Count!=1)return Invalid("ambiguous legacy request: select an individual copy");seq=matches[0].seq;}
        if(!Owns(p,seq)||p.CardIds[(int)seq-1]!=req.cardId)return Invalid("selected card identity mismatch");
        if(InTraining(p,seq))return fail("card is training",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(p.ActiveStage!=null)return Invalid("finish current battle before reset");
        var state=p.CardStates.GetValueOrDefault(seq)??new LocalCardState();var card=catalog.RequireCard(req.cardId);
        var levelRow=catalog.CardResets.FirstOrDefault(x=>x.Level==state.Level);if(levelRow==null)return Invalid("invalid card level");
        long refund=0,points=levelRow.CardLevelRewardPoint;bool changed=state.Level>=catalog.Numbers[BscTable.NUMBER_TYPE.CARD_RESET_LEVEL];
        for(int slot=0;slot<7;slot++) {
            int index=slot<2?slot:slot-2;var ids=slot<2?card.ActiveSkillId:card.PassiveSkillId;var levels=slot<2?state.ActiveSkills:state.PassiveSkills;
            if(index>=ids.Count||index>=levels.Count||!catalog.SkillIds.Contains(ids[index]))continue;
            var row=catalog.CardResets.FirstOrDefault(x=>x.Level==levels[index]);if(row==null)return Invalid("invalid skill level");
            if(slot<row.CardSkillPaybackGold.Count)refund+=row.CardSkillPaybackGold[slot];
            if(slot<row.CardSkillRewardPoint.Count)points+=row.CardSkillRewardPoint[slot];
            changed|=levels[index]>=2;
        }
        if(!changed)return fail("card already has baseline level and skills",ServerErrorCode.ERROR_CARD_RESET_INVALID_CARD_LEVEL);
        var reward=catalog.CardResetRewards.FirstOrDefault(x=>x.MinRewardPoint<=points&&x.MaxRewardPoint>=points);var cost=catalog.CardResets.FirstOrDefault(x=>x.Level==reward?.Id);
        if(reward==null||cost==null||cost.CostItemIdType.Count!=cost.CostItemId.Count||cost.CostItemId.Count!=cost.CostItemQuantity.Count||reward.ItemId.Count!=reward.ItemQuantity.Count||reward.ItemId.Count!=reward.RewardProb.Count||reward.ItemIdType.Any(x=>x!=BscTable.REWARD_TYPE.REWARD_TYPE_ITEM))return Invalid("invalid reward/cost row");
        var needs=new Dictionary<uint,int>();long gold=0,cash=0;
        for(int i=0;i<cost.CostItemId.Count;i++) {
            int n=cost.CostItemQuantity[i];if(n<0)return Invalid("invalid cost quantity");
            switch(cost.CostItemIdType[i]) {
                case BscTable.COST_TYPE.COST_TYPE_ITEM:AddMaterial(needs,(uint)cost.CostItemId[i],n);break;
                case BscTable.COST_TYPE.COST_TYPE_GOLD:gold+=n;break;
                case BscTable.COST_TYPE.COST_TYPE_CASH:cash+=n;break;
                case BscTable.COST_TYPE.COST_NONE:break;
                default:return Invalid("unsupported reset currency");
            }
        }
        var equipment=p.EquipmentItems.Where(x=>x.Sequence==state.WeaponSequence||state.AccessorySequences.Contains(x.Sequence)).ToList();
        foreach(var owned in equipment)gold+=UnwearCost(catalog.Items.First(x=>x.Id==owned.ItemId));
        var returned=new Dictionary<uint,int>();foreach(uint id in state.GemIds.Where(x=>x!=0)){var item=catalog.Items.FirstOrDefault(x=>x.Id==id);if(item==null)return Invalid("invalid equipped gem");gold+=UnwearCost(item);AddMaterial(returned,id,1);}
        if(gold>p.Gold||cash>p.CashPoint||needs.Any(x=>p.Inventory.GetValueOrDefault(x.Key)<x.Value))return fail("not enough reset/unequip costs",ServerErrorCode.ERROR_CARD_RESET_NOT_ENOUGH_COST);
        int rewardIndex=TranscendenceRoll(Enumerable.Range(0,reward.ItemId.Count).ToArray(),reward.RewardProb);
        uint rewardId=(uint)reward.ItemId[rewardIndex];int rewardCount=reward.ItemQuantity[rewardIndex];
        if(rewardCount<1||!catalog.Items.Any(x=>x.Id==rewardId))return Invalid("invalid reward item");
        var next=p.Inventory.ToDictionary();foreach(var x in needs)next[x.Key]=next.GetValueOrDefault(x.Key)-x.Value;
        bool Grant(uint id,int count){long n=(long)next.GetValueOrDefault(id)+count;if(n>int.MaxValue||n<0)return false;next[id]=(int)n;return true;}
        foreach(var x in returned)if(!Grant(x.Key,x.Value))return fail("gem inventory full",ServerErrorCode.ERROR_CARD_RESET_NOT_ENOUGH_INVEN);
        if(!Grant(rewardId,rewardCount)||!Grant(GrowupItem,levelRow.CardLevelPaybackExp)||refund<0||p.Gold-(ulong)gold+(ulong)refund>uint.MaxValue)return fail("refund capacity exceeded",ServerErrorCode.ERROR_CARD_RESET_NOT_ENOUGH_INVEN);
        p.Inventory=next;p.Gold=(uint)(p.Gold-(ulong)gold+(ulong)refund);p.CashPoint-=(uint)cash;
        state.Level=1;state.Exp=ProgressionRules.CardFloor(catalog,1);state.ActiveSkills=state.ActiveSkills.Select(_=>1u).ToList();state.PassiveSkills=state.PassiveSkills.Select(_=>1u).ToList();
        state.WeaponSequence=0;state.AccessorySequences=[0,0];state.GemIds=[0,0,0];
        p.CardStates[seq]=state;
        var r=new GsCardResetOk{resetCardInfo=CardInfo(req.cardId,seq,p),paybackLevel_Exp=(uint)levelRow.CardLevelPaybackExp,paybackSkill_Gold=(uint)refund,paybackTotalRewardPoint=(uint)points,currCoinInfo=Coins(p),rewardItem=ItemInfo(rewardId,rewardCount)};
        r.deltaItemList.AddRange(needs.Select(x=>ItemInfo(x.Key,-x.Value)));r.unequipItemList.AddRange(returned.Select(x=>ItemInfo(x.Key,x.Value)));
        foreach(var owned in equipment){var item=EquipmentInfo(p,owned);item.itemCount=0;r.unequipItemList.Add(item);}return ok(r);
    }
}
