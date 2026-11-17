internal sealed partial class ProtocolRouter
{
    // Version the offered build, never reinterpret an already previewed template.
    private const int CompetitiveLadderVersion=1;
    private static bool HasCompetitiveLadder(uint mode)=>mode is 10 or 12 or 31 or 42;
    private void RefreshCompetitiveLadderOffers(LocalPlayer p,uint mode)
    {
        if(HasCompetitiveLadder(mode)&&p.ActiveStage==null&&p.Competitive.ActiveMode==0&&
            p.Competitive.Offers.TryGetValue(mode,out var offers)&&offers.Any(x=>x.LadderVersion<CompetitiveLadderVersion))
            p.Competitive.Offers.Remove(mode);
    }
    private void ApplyCompetitiveLadder(LocalPlayer p,uint mode,List<LocalCompetitiveOffer> offers)
    {
        if(!HasCompetitiveLadder(mode))return;
        var progress=CompetitiveProgression(p,mode);
        foreach(var offer in offers){
            offer.LadderVersion=CompetitiveLadderVersion;
            // Keep low-ladder introductory matches intact. An established front
            // rank cannot become a beginner again through level/loss protection.
            if(progress.Battles<5&&progress.Rank>1000)continue;
            int floor=offer.Rank<=100?4:offer.Rank<=300?3:offer.Rank<=1000?2:offer.Rank<=5000?1:0;
            offer.Grade=Math.Max(offer.Grade,floor);
            uint levelFloor=offer.Rank<=300?99u:offer.Rank<=1000?90u:0u;
            offer.Level=Math.Min(ProgressionRules.PlayerMax(catalog),Math.Max(offer.Level,levelFloor));
            offer.GrowthStage=offer.Rank<=10?5:offer.Rank<=30?4:offer.Rank<=100?3:offer.Rank<=300?2:offer.Rank<=1000?1:0;
            var source=Bots.Find(offer.Uid)!;
            offer.Name=source.Profile.Nickname+" · "+CompetitiveGradeNames[offer.Grade]+"人机";
        }
    }
    private void ApplyCompetitiveGrowth(LocalPlayer bot,LocalCompetitiveOffer offer)
    {
        if(offer.LadderVersion<1||offer.GrowthStage<=0)return;
        int stage=Math.Clamp(offer.GrowthStage,1,5);
        if(FeatureOpen(bot,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_SET_EMPOWER))
        foreach(var owned in bot.EquipmentItems){
            var gear=catalog.ItemsById[(int)owned.ItemId];
            if(!gear.Empower||gear.EmpowerMaxCost<=0||gear.Rank<catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.EMPOWERSTONE_LIMIT_RANK))continue;
            var owner=bot.CardStates.Single(x=>x.Value.WeaponSequence==owned.Sequence||x.Value.AccessorySequences.Contains(owned.Sequence));
            var card=catalog.RequireCard(bot.CardIds[(int)owner.Key-1]);
            var attribute=gear.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON?
                (card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE?BscTable.EQEFFECT_TYPE.EQEFFECT_F_ATTACK:BscTable.EQEFFECT_TYPE.EQEFFECT_P_ATTACK):BscTable.EQEFFECT_TYPE.EQEFFECT_HP;
            int budget=gear.EmpowerMaxCost*stage/5;
            var stone=catalog.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&x.EmpowerValue>0&&x.EmpowerValue<=budget&&
                x.EmpowerLimitItemLevel<=gear.Level&&x.AttributeValueIdType.Count==1&&x.AttributeValueIdType[0]==attribute&&x.AttributeValueId.Count==1&&x.AttributeValueId[0]>0)
                .OrderByDescending(x=>(double)x.AttributeValueId[0]/x.EmpowerValue).ThenBy(x=>x.Id).FirstOrDefault();
            if(stone==null)continue;
            uint count=(uint)(budget/stone.EmpowerValue);
            owned.CurrentEmpowerValue=count*(uint)stone.EmpowerValue;
            owned.EmpowerValues=Enumerable.Repeat(0u,EngravingRules.Attributes.Length).ToList();
            owned.EmpowerValues[Array.IndexOf(EngravingRules.Attributes,attribute)]=count*(uint)stone.AttributeValueId[0];
        }
        if(!FeatureOpen(bot,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_TRANSCENDENCE))return;
        foreach(var pair in bot.CardStates){
            var card=catalog.RequireCard(bot.CardIds[(int)pair.Key-1]);
            if(card.Rank<catalog.Numbers[BscTable.NUMBER_TYPE.TRANSCENDENCE_CONTENT_ON])continue;
            var rule=catalog.Transcendence.FirstOrDefault(x=>x.CardId==card.Id);if(rule==null)continue;
            // Reach each original prerequisite before exposing dependent effects.
            for(int pass=0;pass<rule.EffectDataList.Count;pass++)foreach(var effect in rule.EffectDataList){
                if(pair.Value.TranscendenceEffects.Any(x=>x.Id==effect.Id)||effect.EffectGradeDataList.Count==0)continue;
                if(!(effect.UseCondition1<=1&&effect.UseCondition2<=1)&&pair.Value.TranscendenceEffects.Count(x=>x.Grade>=effect.UseCondition2)<effect.UseCondition1)continue;
                int grade=Math.Min(stage,effect.EffectGradeDataList.Count);
                pair.Value.TranscendenceEffects.Add(new(){Id=(uint)effect.Id,Grade=(uint)grade,Value=(uint)effect.EffectGradeDataList[grade-1].EffectMax});
            }
        }
    }
}
