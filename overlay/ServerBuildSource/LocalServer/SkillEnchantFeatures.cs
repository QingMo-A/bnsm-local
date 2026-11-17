using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    private static uint[] SkillEnchantSlots(LocalCardState state)=>Enumerable.Range(0,7)
        .Select(i=>state.SkillEnchants!=null&&i<state.SkillEnchants.Count?state.SkillEnchants[i]:0u).ToArray();

    private Reply EnchantSkill(GsSkillEnchantRequest request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_SKILL_ENCHANT_REQUEST);
        if(!Owns(p,request.cardSeq))return fail("card is not owned",ServerErrorCode.ERROR_INVALID_CARD);
        if(InTraining(p,request.cardSeq))return fail("finish training before changing skills",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(p.ActiveStage!=null)return Invalid("finish the current battle before changing skills");
        // The original UI gates PLAYER level, not the selected card's level.
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SKILL_ENCHANT))return fail("skill enchant requires the original player level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        var card=catalog.RequireCard(p.CardIds[(int)request.cardSeq-1]);var state=CardState(p,request.cardSeq);
        int slot=(int)request.skillType,index=slot<2?slot:slot-2;
        if(slot<0||slot>=7||slot>=card.SkillEnchantGroupId.Count||!catalog.SkillEnchants.TryGetValue(card.SkillEnchantGroupId[slot],out var group))return Invalid("this skill has no original enchant path at the current star rank");
        if(state.Enchant<catalog.Numbers[BscTable.NUMBER_TYPE.SKILL_ENCHANT_LIMIT_CARD_ENCHANT_GRADE])return fail("card enchant grade is too low",ServerErrorCode.ERROR_INVALID_ENCHANT_VALUE);
        var ids=slot<2?card.ActiveSkillId:card.PassiveSkillId;
        if(index>=ids.Count||!catalog.SkillIds.Contains(ids[index])||card.Rank<catalog.Numbers[SkillOpenRank[slot]])return Invalid("skill is not unlocked");
        var enchants=SkillEnchantSlots(state);uint current=enchants[slot];
        if(current>=catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_SKILL_ENCHANT))return fail("skill enchant reached the original version limit",ServerErrorCode.ERROR_OVER_MAX_ENCHANT_LIMIT);
        int previous=current==0?ids[index]:group.SkillEnchantInfoList.FirstOrDefault(x=>x.EnchantLevel==current)?.NextEnchantSkillId??0;
        var rule=group.SkillEnchantInfoList.SingleOrDefault(x=>x.PrevEnchantSkillId==previous&&x.EnchantLevel==current+1);
        if(rule==null||!catalog.SkillIds.Contains(rule.NextEnchantSkillId)||rule.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_GOLD||rule.Cost<0||rule.Prob<0||rule.Prob>100||rule.MaterialItemId.Count!=rule.MaterialItemQuantity.Count)return Invalid("invalid original skill enchant recipe");
        if(p.Gold<rule.Cost)return fail("not enough gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
        var needs=new Dictionary<uint,int>();
        for(int i=0;i<rule.MaterialItemId.Count;i++) {
            if(rule.MaterialItemId[i]<=0||rule.MaterialItemQuantity[i]<=0||!catalog.ItemsById.ContainsKey(rule.MaterialItemId[i]))return Invalid("invalid original material");
            AddMaterial(needs,(uint)rule.MaterialItemId[i],rule.MaterialItemQuantity[i]);
        }
        const uint universal=LocalCardRegistry.UniversalFragmentId;
        bool allowsUniversal=LocalCardRegistry.AllowsUniversalFragments(card);
        uint piece=(uint)card.PieceId;
        if(piece!=universal&&needs.TryGetValue(piece,out int required)&&catalog.ItemsById[(int)piece].ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE)
        {
            int owned=Math.Clamp(p.Inventory.GetValueOrDefault(piece),0,required);
            if(owned<required)
            {
                if(!allowsUniversal)return fail(LocalCardRegistry.UniversalFragmentsDisabledMessage,ServerErrorCode.ERROR_NOT_ENOUGH_MATERIAL);
                if(owned==0)needs.Remove(piece);
                else needs[piece]=owned;
                AddMaterial(needs,universal,required-owned);
            }
        }
        if(needs.Any(x=>p.Inventory.GetValueOrDefault(x.Key)<x.Value))return fail("not enough skill enchant materials",ServerErrorCode.ERROR_NOT_ENOUGH_MATERIAL);
        bool success=RandomNumberGenerator.GetInt32(100)<rule.Prob;
        p.Gold-=(uint)rule.Cost;
        foreach(var material in needs)p.Inventory[material.Key]-=material.Value;
        if(success)enchants[slot]=(uint)rule.EnchantLevel;
        state.SkillEnchants=enchants.ToList();
        var result=new GsSkillEnchantOk{cardInfo=CardInfo((uint)card.Id,request.cardSeq,p),coinInfo=Coins(p),result=success?CraftResult.CRAFT_RESULT_SUCCESS:CraftResult.CRAFT_RESULT_FAIL};
        result.deltaItemList.AddRange(needs.Select(x=>ItemInfo(x.Key,-x.Value)));
        // Growth receipts persist the result and mutation together, including a failed roll.
        return ok(result);
    }
}
