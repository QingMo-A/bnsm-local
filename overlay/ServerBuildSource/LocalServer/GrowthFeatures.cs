internal sealed partial class ProtocolRouter
{
    private void RefreshSkillPoints(LocalPlayer p)
    {
        uint max=catalog.SkillPointMax(p);ulong now=Now;
        var old=(p.SkillPoint,p.SkillPointStamp);
        if(p.SkillPoint==null) {p.SkillPoint=max;p.SkillPointStamp=now;}
        if(p.SkillPointStamp==0 || p.SkillPointStamp>now)p.SkillPointStamp=now;
        ulong period=(ulong)Math.Max(1,catalog.Numbers[BscTable.NUMBER_TYPE.SKILLPOINT_RECOVERY_TIME]);
        if(p.SkillPoint>=max) {p.SkillPoint=max;p.SkillPointStamp=now;}
        else {
            ulong elapsed=(now-p.SkillPointStamp)/period;
            if(elapsed>0) {p.SkillPoint=(uint)Math.Min(max,p.SkillPoint!.Value+elapsed);p.SkillPointStamp=p.SkillPoint==max ? now : p.SkillPointStamp+elapsed*period;}
        }
        if(old.Item1!=p.SkillPoint || (p.SkillPoint<max && old.Item2!=p.SkillPointStamp))store.Save();
    }
    private bool FeatureOpen(LocalPlayer p,BscTable.CONTENT_OPEN_TYPE type) => p.Level>=catalog.ContentValue(type);
    // Match the APK's UI order: active 1, passive 1/2/3, active 2, passive 4/5.
    private static readonly int[] SkillUiIndex=[0,4,1,2,3,5,6];
    private static readonly BscTable.NUMBER_TYPE[] SkillOpenRank=[BscTable.NUMBER_TYPE.ACTIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.ULTIMATE_SKILL_OPEN_GRADE,
        BscTable.NUMBER_TYPE.PASSIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_2_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_3_OPEN_GRADE,
        BscTable.NUMBER_TYPE.PASSIVE_SKILL_4_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_5_OPEN_GRADE];
    private sealed record MaterialPlan(Dictionary<uint,int> Inventory,ulong Gold,List<PKUserItem> Deltas);
    private MaterialPlan? PlanMaterials(LocalPlayer p,Dictionary<uint,int> needs,ulong gold)
    {
        var plan=new CraftPlan(p,catalog,false);
        if(gold>long.MaxValue||!plan.Cost((long)gold))return null;
        foreach(var pair in needs)if(!plan.Consume(pair.Key,pair.Value,true))return null;
        var deltas=plan.Inventory.Keys.Union(p.Inventory.Keys).Order().Select(id=>ItemInfo(id,plan.Inventory.GetValueOrDefault(id)-p.Inventory.GetValueOrDefault(id))).Where(x=>x.itemCount!=0).ToList();
        return new(plan.Inventory,(ulong)plan.Gold,deltas);
    }
    private static void AddMaterial(Dictionary<uint,int> needs,uint id,int count) {
        if(id==0 || count<0)throw new InvalidDataException("Invalid original material requirement");
        if(count>0)needs[id]=checked(needs.GetValueOrDefault(id)+count);
    }
    private Reply? HandleGrowth(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_SKILL_INFO);
        if(name=="GS_SKILL_ENCHANT_REQUEST")return EnchantSkill((GsSkillEnchantRequest)request!,p,ok,fail);
        if(name=="GS_CARD_SKILL_LEVELUP_REQUEST") {
            var upgrade=(GsCardSkillLevelUpRequest)request!;
            if(!Owns(p,upgrade.targetCardSeq))return Invalid("card is not owned");
            if(InTraining(p,upgrade.targetCardSeq))return fail("finish training before changing skills",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            if(p.ActiveStage!=null)return fail("finish the current battle before changing skills",ServerErrorCode.ERROR_INVALID_CARD);
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_SKILLUP))return fail("skill upgrade unlocks at the original player level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            var state=CardState(p,upgrade.targetCardSeq);var data=catalog.RequireCard(p.CardIds[(int)upgrade.targetCardSeq-1]);
            if(upgrade.levelUpInfo.Count==0 || upgrade.levelUpInfo.Count>7 || upgrade.levelUpInfo.Select(x=>x.skillType).Distinct().Count()!=upgrade.levelUpInfo.Count)return Invalid("invalid or duplicate skill slots");
            var active=state.ActiveSkills.ToList();var passive=state.PassiveSkills.ToList();ulong gold=0,points=0;
            foreach(var change in upgrade.levelUpInfo) {
                int slot=(int)change.skillType;
                if(slot<0 || slot>=7 || change.levelupValue==0 || change.levelupValue>99)return Invalid("invalid skill slot or increment");
                int index=slot<2 ? slot : slot-2;var ids=slot<2 ? data.ActiveSkillId : data.PassiveSkillId;
                var levels=slot<2 ? active : passive;
                if(index>=ids.Count || index>=levels.Count || !catalog.SkillIds.Contains(ids[index]) || data.Rank<catalog.Numbers[SkillOpenRank[slot]])return Invalid("skill has not unlocked at this star rank");
                var costs=catalog.CardProcesses.GetSkillItem(SkillUiIndex[slot])?.ProcessDataList;
                ulong target=(ulong)levels[index]+change.levelupValue;
                if(costs==null || levels[index]<1 || target>(uint)costs.Count || target>state.Level || target>catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_SKILL_LEVEL])
                    return fail("skill level cannot exceed card level or original limit",ServerErrorCode.ERROR_SKILL_LEVEL_CANNOT_OVER_CARD_LEVEL);
                for(int level=(int)levels[index];level<(int)target;level++)gold+=(uint)Math.Max(0,costs[level-1].CostGold);
                points+=change.levelupValue;levels[index]=(uint)target;
            }
            if(points>(p.SkillPoint??0))return fail("not enough skill points",ServerErrorCode.ERROR_NOT_ENOUGH_SKILL_POINT);
            if(gold>p.Gold)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            state.ActiveSkills=active;state.PassiveSkills=passive;p.Gold-=(uint)gold;p.SkillPoint-=(uint)points;
            return ok(new GsCardSkillLevelUpOk {cardInfo=CardInfo((uint)data.Id,upgrade.targetCardSeq,p),currentCoinInfo=Coins(p),remainSkillPoint=p.SkillPoint??0,skillpointTimeStamp=p.SkillPointStamp});
        }
        if(name=="GS_USER_SKILL_POINT_CHARGE_REQUEST") {
            var vip=catalog.VipFor(p.VipExp);
            if(p.SkillPointChargeCount>=vip.SkillPointMaxResetCount)return fail("daily original VIP skill refill limit",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
            if(p.SkillPoint>=catalog.SkillPointMax(p))return fail("skill points are already full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var cost=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_RESET_SKILL_POINT,p.SkillPointChargeCount+1);
            if(cost==null || cost.Value<0)return Invalid("original refill price is unavailable");
            if(p.CashPoint<cost.Value)return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)cost.Value;p.SkillPoint=catalog.SkillPointMax(p);p.SkillPointStamp=Now;p.SkillPointChargeCount++;
            return ok(new GsUserSkillPointChargeOk {nowSkillPoint=p.SkillPoint.Value,currentCoinInfo=Coins(p),exchangeToSkillPointCount=p.SkillPointChargeCount});
        }
        if(name is not ("GS_ENCHANT_CARD_REQUEST" or "GS_EVOLUTION_CARD_REQUEST"))return null;
        bool evolve=name=="GS_EVOLUTION_CARD_REQUEST";
        ulong seq=evolve ? ((GsCardEvolutionRequest)request!).targetCardSeq : ((GsCardEnchantRequest)request!).targetCardSeq;
        if(!Owns(p,seq))return fail("card is not owned",ServerErrorCode.ERROR_INVALID_CARD);
        if(InTraining(p,seq))return fail("finish training before changing this card",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(p.ActiveStage!=null)return fail("finish the current battle before changing this card",ServerErrorCode.ERROR_INVALID_CARD);
        if(!FeatureOpen(p,evolve ? BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_EVOLVE : BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_ENCHANT))return fail("upgrade unlocks at the original player level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var card=catalog.RequireCard(p.CardIds[(int)seq-1]);var current=CardState(p,seq);var needs=new Dictionary<uint,int>();
        var process=catalog.CardProcesses.List.FirstOrDefault(x=>x.ProcessType==(evolve ? BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_CARD_EVOLUTION : BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_CARD_ENCHANT));
        BscTable.CardData? next=null;int costIndex;
        if(evolve) {
            next=catalog.UsableHeroes.FirstOrDefault(c=>c.Id==card.RankUpCardId && c.EvolveFirstAncestor==card.EvolveFirstAncestor && c.Rank==card.Rank+1);
            int max=Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_RANK],catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE));
            if(next==null || card.Rank>=max || card.Rank>=card.MaxRank)return fail("card has reached its original evolution limit",ServerErrorCode.ERROR_OVER_MAX_RANK_EVOLUTION_LIMIT);
            if(current.Level<catalog.CardRanks.First(x=>x.Rank==next.Rank).MinLevel)return fail("card level is too low",ServerErrorCode.ERROR_NOT_ENOUGH_CARD_LEVEL);
            int pieces=catalog.CardRanks.First(x=>x.Rank==card.Rank).NextEVP;
            if(card.PieceId<=0 || pieces<=0)return fail("original evolution fragment data is absent",ServerErrorCode.ERROR_INVALID_CARD);
            int own=Math.Clamp(p.Inventory.GetValueOrDefault((uint)card.PieceId),0,pieces);
            if(own<pieces&&!LocalCardRegistry.AllowsUniversalFragments(card))
                return fail(LocalCardRegistry.UniversalFragmentsDisabledMessage,ServerErrorCode.ERROR_NOT_ENOUGH_MATERIAL);
            AddMaterial(needs,(uint)card.PieceId,own);AddMaterial(needs,LocalCardRegistry.UniversalFragmentId,pieces-own);
            if(card.RankUpMaterialItemId.Count!=card.RankUpMaterialItemCount.Count)return fail("invalid original evolution material list",ServerErrorCode.ERROR_INVALID_CARD);
            for(int i=0;i<card.RankUpMaterialItemId.Count;i++)AddMaterial(needs,(uint)card.RankUpMaterialItemId[i],card.RankUpMaterialItemCount[i]);
            costIndex=card.Rank; // APK popup indexes by the CURRENT rank, not rank - 1.
        } else {
            var materials=catalog.CardEnchants.FirstOrDefault(x=>x.EnchantType==card.EnchantType)?.MaterialDataList;
            if(materials==null || current.Enchant>=materials.Count || current.Enchant>=catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_ENCHANT))return fail("card enchant limit",ServerErrorCode.ERROR_OVER_MAX_ENCHANT_LIMIT);
            var material=materials[(int)current.Enchant];
            if(current.Level<material.MinLevel)return fail("card level is too low",ServerErrorCode.ERROR_NOT_ENOUGH_CARD_LEVEL);
            if(material.ItemId.Count!=material.Count.Count)return fail("invalid original enchant materials",ServerErrorCode.ERROR_INVALID_CARD);
            for(int i=0;i<material.ItemId.Count;i++)AddMaterial(needs,(uint)material.ItemId[i],material.Count[i]);
            costIndex=(int)current.Enchant;
        }
        if(process==null || costIndex>=process.ProcessDataList.Count)return fail("missing original upgrade price",ServerErrorCode.ERROR_INVALID_CARD);
        var price=process.ProcessDataList[costIndex];
        if(price.Probability!=100 || price.CostCash!=0 || price.CostSoulstone!=0 || price.CostGold<0)return fail("this original upgrade rule is not supported locally",ServerErrorCode.ERROR_INVALID_CARD);
        var plan=PlanMaterials(p,needs,(uint)price.CostGold);
        if(plan==null)return fail("not enough upgrade materials or fragments",ServerErrorCode.ERROR_NOT_ENOUGH_MATERIAL);
        if(plan.Gold>p.Gold)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        p.Gold-=(uint)plan.Gold;p.Inventory=plan.Inventory;
        if(evolve) {
            p.CollectedCardIds.Add((uint)card.Id);p.CardIds[(int)seq-1]=(uint)next!.Id;p.CollectedCardIds.Add((uint)next.Id);
            var result=new GsCardEvolutionOk {evolutionCardInfo=CardInfo((uint)next.Id,seq,p),currentCoinInfo=Coins(p)};result.useitemList.AddRange(plan.Deltas);return ok(result);
        }
        current.Enchant++;
        var enchant=new GsCardEnchantOk {enchantCardInfo=CardInfo((uint)card.Id,seq,p),currentCoinInfo=Coins(p),successFlag=CardProcessSuccessFlag.CARD_PROCESS_SUCCESS};enchant.itemList.AddRange(plan.Deltas);return ok(enchant);
    }
}
