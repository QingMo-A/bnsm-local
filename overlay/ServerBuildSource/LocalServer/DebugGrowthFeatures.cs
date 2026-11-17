internal sealed partial class ProtocolRouter
{
    private Reply? HandleDebugGrowth(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        uint flags=LocalDebugFlags.Normalize(p.DebugFlags);
        uint free=name switch {"GS_LEVELUP_CARD_REQUEST"=>LocalDebugFlags.FreeLevel,"GS_ENCHANT_CARD_REQUEST"=>LocalDebugFlags.FreeEnchant,"GS_CARD_SKILL_LEVELUP_REQUEST"=>LocalDebugFlags.FreeSkill,"GS_EVOLUTION_CARD_REQUEST"=>LocalDebugFlags.FreeBreakthrough|LocalDebugFlags.FreeAwakening,_=>0};
        if((flags&free)==0)return null;
        ulong seq=name=="GS_LEVELUP_CARD_REQUEST"?((GsCardLevelUpRequest)request!).targetCardSeq:name=="GS_CARD_SKILL_LEVELUP_REQUEST"?((GsCardSkillLevelUpRequest)request!).targetCardSeq:name=="GS_EVOLUTION_CARD_REQUEST"?((GsCardEvolutionRequest)request!).targetCardSeq:((GsCardEnchantRequest)request!).targetCardSeq;
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_CARD);
        if(!Owns(p,seq))return Invalid("card is not owned");
        if(InTraining(p,seq))return fail("card is training",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(p.ActiveStage!=null)return Invalid("finish current battle first");
        var card=catalog.RequireCard(p.CardIds[(int)seq-1]);var state=CardState(p,seq);
        if(name=="GS_LEVELUP_CARD_REQUEST") {
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_LEVELUP))return fail("original feature level gate",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(state.Level>=99)return fail("card reached level 99",ServerErrorCode.ERROR_CARD_LEVEL_IS_OVER_LIMIT);
            bool max=(flags&LocalDebugFlags.MaxLevel)!=0;uint target=max?ProgressionRules.CardCeiling(catalog,99):ProgressionRules.CardFloor(catalog,state.Level+1);
            state.Exp=target;state.Level=ProgressionRules.CardLevel(catalog,target,99);
            return ok(new GsCardLevelUpOk{levelupCardInfo=CardInfo((uint)card.Id,seq,p),currentCoinInfo=Coins(p),afterExpItemCount=(uint)Math.Max(0,p.Inventory.GetValueOrDefault(GrowupItem)),deltaItemInfo=ItemInfo(GrowupItem,0)});
        }
        if(name=="GS_CARD_SKILL_LEVELUP_REQUEST") {
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_SKILLUP))return fail("original feature level gate",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            var r=(GsCardSkillLevelUpRequest)request!;var active=state.ActiveSkills.ToList();var passive=state.PassiveSkills.ToList();
            if(r.levelUpInfo.Count==0||r.levelUpInfo.Count>7||r.levelUpInfo.Select(x=>x.skillType).Distinct().Count()!=r.levelUpInfo.Count)return Invalid("invalid skill list");
            foreach(var change in r.levelUpInfo) {
                int slot=(int)change.skillType,index=slot<2?slot:slot-2;if(slot<0||slot>6||change.levelupValue==0||change.levelupValue>99)return Invalid("invalid skill increment");
                var ids=slot<2?card.ActiveSkillId:card.PassiveSkillId;var levels=slot<2?active:passive;
                if(index>=ids.Count||index>=levels.Count||!catalog.SkillIds.Contains(ids[index])||card.Rank<catalog.Numbers[SkillOpenRank[slot]])return Invalid("skill is not unlocked");
                int cap=Math.Min(99,catalog.CardProcesses.GetSkillItem(SkillUiIndex[slot])?.ProcessDataList.Count??0);
                if(levels[index]>=cap)return fail("skill at maximum",ServerErrorCode.ERROR_SKILL_LEVEL_CANNOT_OVER_CARD_LEVEL);
                levels[index]=(flags&LocalDebugFlags.MaxSkill)!=0?(uint)cap:(uint)Math.Min(cap,levels[index]+change.levelupValue);
            }
            state.ActiveSkills=active;state.PassiveSkills=passive;
            return ok(new GsCardSkillLevelUpOk{cardInfo=CardInfo((uint)card.Id,seq,p),currentCoinInfo=Coins(p),remainSkillPoint=p.SkillPoint??0,skillpointTimeStamp=p.SkillPointStamp});
        }
        if(name=="GS_ENCHANT_CARD_REQUEST") {
            if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_ENCHANT))return fail("original feature level gate",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            int max=Math.Min(catalog.CardEnchants.FirstOrDefault(x=>x.EnchantType==card.EnchantType)?.MaterialDataList.Count??0,catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_ENCHANT));
            if(state.Enchant>=max)return fail("original enchant maximum",ServerErrorCode.ERROR_OVER_MAX_ENCHANT_LIMIT);
            state.Enchant=(flags&LocalDebugFlags.MaxEnchant)!=0?(uint)max:state.Enchant+1;
            return ok(new GsCardEnchantOk{enchantCardInfo=CardInfo((uint)card.Id,seq,p),currentCoinInfo=Coins(p),successFlag=CardProcessSuccessFlag.CARD_PROCESS_SUCCESS});
        }
        int unit=(int)catalog.Numbers[BscTable.NUMBER_TYPE.CARD_RANK_UNIT_PER_STEP];bool awake=card.Rank>=unit-1;
        free=awake?LocalDebugFlags.FreeAwakening:LocalDebugFlags.FreeBreakthrough;if((flags&free)==0)return null;
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_EVOLVE))return fail("original feature level gate",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        int limit=Math.Min(card.MaxRank,Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_RANK],catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE)));
        if(!awake)limit=Math.Min(limit,unit-1);
        bool maximum=(flags&(awake?LocalDebugFlags.MaxAwakening:LocalDebugFlags.MaxBreakthrough))!=0;
        var next=card;var idsEarned=new List<uint>();var visited=new HashSet<int>{card.Id};
        while(next.Rank<limit) {
            var row=catalog.UsableHeroes.FirstOrDefault(x=>x.Id==next.RankUpCardId&&x.EvolveFirstAncestor==card.EvolveFirstAncestor&&x.Rank==next.Rank+1);
            if(row==null||!visited.Add(row.Id))break;next=row;idsEarned.Add((uint)row.Id);if(!maximum)break;
        }
        if(next.Id==card.Id)return fail("original evolution chain limit",ServerErrorCode.ERROR_OVER_MAX_RANK_EVOLUTION_LIMIT);
        p.CardIds[(int)seq-1]=(uint)next.Id;p.CollectedCardIds.Add((uint)card.Id);foreach(uint id in idsEarned)p.CollectedCardIds.Add(id);
        return ok(new GsCardEvolutionOk{evolutionCardInfo=CardInfo((uint)next.Id,seq,p),currentCoinInfo=Coins(p)});
    }
}
