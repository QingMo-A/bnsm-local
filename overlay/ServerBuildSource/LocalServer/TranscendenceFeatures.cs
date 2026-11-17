using System.Security.Cryptography;

internal sealed class LocalTranscendenceEffect
{
    public uint Id {get;set;}
    public uint Value {get;set;}
    public uint Grade {get;set;}=1;
    public LocalTranscendenceEffect Copy()=>new(){Id=Id,Value=Value,Grade=Grade};
}
internal sealed class LocalTranscendenceUndo
{
    public uint CardId {get;set;}
    public uint ItemId {get;set;}
    public int Count {get;set;}
    public List<LocalTranscendenceEffect> Effects {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    private static int TranscendenceRoll(IReadOnlyList<int> values,IReadOnlyList<int> weights)
    {
        if(values.Count==0||values.Count!=weights.Count||weights.Any(x=>x<0)||weights.Sum(x=>(long)x)<=0)
            throw new InvalidDataException("Invalid recovered transcendence probability table");
        long roll=Random.Shared.NextInt64(weights.Sum(x=>(long)x));
        for(int i=0;i<values.Count;i++){roll-=weights[i];if(roll<0)return values[i];}
        throw new InvalidOperationException();
    }
    private Reply? HandleTranscendence(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_GET_TRANSCENDENCE_INFO_REQUEST" or "GS_APPLY_TRANSCENDENCE_GROWTH_REQUEST" or "GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST" or "GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST"))return null;
        bool mutation=name!="GS_GET_TRANSCENDENCE_INFO_REQUEST";
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(mutation&&p.TranscendenceReceipts.TryGetValue(key,out var receipt))
            return receipt.Fingerprint==fingerprint?new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name):fail("transcendence sequence reused",ServerErrorCode.ERROR_INVALID_ID);
        ulong seq=(ulong)(Field(request,"cardSeq")??0UL);
        if(!Owns(p,seq))return fail("card is not owned",ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE);
        uint id=p.CardIds[(int)seq-1];var data=catalog.RequireCard(id);
        var rule=catalog.Transcendence.FirstOrDefault(x=>x.CardId==id);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_TRANSCENDENCE))return fail("transcendence is not open",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(data.Rank<catalog.Numbers[BscTable.NUMBER_TYPE.TRANSCENDENCE_CONTENT_ON])return fail("card has not reached the original transcendence star tier",ServerErrorCode.ERROR_NOT_ENOUGH_CARD_RANK);
        if(rule==null||rule.EffectDataList.Count==0)return fail("this card has no original transcendence data",ServerErrorCode.ERROR_NOT_EXIST_TRANSCENDENCE_INFO);
        var state=CardState(p,seq);
        if(!mutation)return ok(new GsGetTranscendenceInfoOk{cardInfo=CardInfo(id,seq,p)});
        if(InTraining(p,seq))return fail("card is training",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(p.ActiveStage!=null)return fail("finish the current battle before transcendence",ServerErrorCode.ERROR_INVALID_CARD);
        Reply Invalid()=>fail("transcendence conditions are not met",ServerErrorCode.ERROR_INVALID_TRANSCENDENCE_GROWTH_CONDITION);
        bool CanPay(int item,int amount)=>item>0&&amount>=0&&p.Inventory.GetValueOrDefault((uint)item)>=amount;
        var deltas=new List<PKUserItem>();var earned=new List<PKUserItem>();
        void Spend(int item,int count){p.Inventory[(uint)item]=p.Inventory.GetValueOrDefault((uint)item)-count;if(count>0)deltas.Add(ItemInfo((uint)item,-count));}
        Reply reply;
        if(name=="GS_CANCEL_TRANSCENDENCE_GROWTH_REQUEST") {
            var undo=state.TranscendenceUndo;
            if(undo==null||undo.CardId!=id)return Invalid();
            if(!CanPay(rule.GrowthCancelItemId,rule.GrowthCancelItemValue))return fail("not enough cancellation materials",ServerErrorCode.ERROR_DO_NOT_HAVE_ENOUGH_CONSUMABLE_ITEM);
            long remaining=p.Inventory.GetValueOrDefault(undo.ItemId)+(long)undo.Count-(undo.ItemId==rule.GrowthCancelItemId?rule.GrowthCancelItemValue:0);
            if(remaining>int.MaxValue)return fail("refund inventory capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            Spend(rule.GrowthCancelItemId,rule.GrowthCancelItemValue);
            p.Inventory[undo.ItemId]=p.Inventory.GetValueOrDefault(undo.ItemId)+undo.Count;earned.Add(ItemInfo(undo.ItemId,undo.Count));
            state.TranscendenceEffects=undo.Effects.Select(x=>x.Copy()).ToList();state.TranscendenceUndo=null;
            var result=new GsCancelTranscendenceGrowthOk{cardInfo=CardInfo(id,seq,p)};result.consumableItemDeltaList.AddRange(deltas);result.earnItemDeltaList.AddRange(earned);
            result.consumableItemRemainList.Add(ItemInfo((uint)rule.GrowthCancelItemId,p.Inventory.GetValueOrDefault((uint)rule.GrowthCancelItemId)));result.earnItemRemainList.Add(ItemInfo(undo.ItemId,p.Inventory[undo.ItemId]));reply=ok(result);
        } else if(name=="GS_BREAK_THROUGH_TRANSCENDENCE_GROWTH_REQUEST") {
            uint effectId=((GsBreakThroughTranscendenceGrowthRequest)request!).effectDataId;
            var effect=state.TranscendenceEffects.FirstOrDefault(x=>x.Id==effectId);var row=rule.EffectDataList.FirstOrDefault(x=>x.Id==effectId);
            if(effect==null||row==null||effect.Grade<1||effect.Grade>=row.EffectGradeDataList.Count)return Invalid();
            var current=row.EffectGradeDataList[(int)effect.Grade-1];var next=row.EffectGradeDataList[(int)effect.Grade];
            if(effect.Value!=current.EffectMax)return fail("fill the current attribute before breakthrough",ServerErrorCode.ERROR_NOT_ENOUGH_GROWTH_VALUE);
            if(!CanPay(next.GradeItemId,next.GradeValue))return fail("not enough breakthrough materials",ServerErrorCode.ERROR_DO_NOT_HAVE_ENOUGH_CONSUMABLE_ITEM);
            int bonus=TranscendenceRoll(next.GradeSuccessBouns,next.GradeSuccessPer);
            uint value=(uint)Math.Clamp((long)effect.Value+bonus,row.EffectMin,next.EffectMax);
            Spend(next.GradeItemId,next.GradeValue);effect.Grade++;effect.Value=value;state.TranscendenceUndo=null;
            var result=new GsBreakThroughTranscendenceGrowthOk{cardInfo=CardInfo(id,seq,p)};result.consumableItemDeltaList.AddRange(deltas);result.consumableItemRemainList.Add(ItemInfo((uint)next.GradeItemId,p.Inventory[(uint)next.GradeItemId]));reply=ok(result);
        } else {
            // Original material prices, attribute caps and weighted gains survive in the client table.
            // Applying one weighted roll to every unlocked attribute is the explicit local algorithm.
            var before=state.TranscendenceEffects.Select(x=>x.Copy()).ToList();var after=before.Select(x=>x.Copy()).ToList();bool eligible=false;
            foreach(var row in rule.EffectDataList) {
                var effect=after.FirstOrDefault(x=>x.Id==row.Id);
                if(effect==null) {
                    bool unlocked=row.UseCondition1<=1&&row.UseCondition2<=1||before.Count(x=>x.Grade>=row.UseCondition2)>=row.UseCondition1;
                    if(!unlocked)continue;
                    effect=new(){Id=(uint)row.Id,Grade=1,Value=(uint)Math.Max(0,row.EffectMin)};after.Add(effect);
                }
                if(effect.Grade<1||effect.Grade>row.EffectGradeDataList.Count)return Invalid();
                var grade=row.EffectGradeDataList[(int)effect.Grade-1];if(effect.Value>=grade.EffectMax)continue;
                eligible=true;int diff=TranscendenceRoll(grade.GrowthValue,grade.GrowthValuePer);
                effect.Value=(uint)Math.Clamp((long)effect.Value+diff,row.EffectMin,grade.EffectMax);
            }
            if(!eligible)return Invalid();
            if(!CanPay(rule.GrowthItemId,rule.GrowthItemValue))return fail("not enough growth materials",ServerErrorCode.ERROR_DO_NOT_HAVE_ENOUGH_CONSUMABLE_ITEM);
            Spend(rule.GrowthItemId,rule.GrowthItemValue);
            state.TranscendenceUndo=new(){CardId=id,ItemId=(uint)rule.GrowthItemId,Count=rule.GrowthItemValue,Effects=before};state.TranscendenceEffects=after;
            var result=new GsApplyTranscendenceGrowthOk{cardInfo=CardInfo(id,seq,p)};result.consumableItemDeltaList.AddRange(deltas);result.consumableItemRemainList.Add(ItemInfo((uint)rule.GrowthItemId,p.Inventory[(uint)rule.GrowthItemId]));reply=ok(result);
        }
        if(p.TranscendenceReceipts.Count>=256)p.TranscendenceReceipts.Remove(p.TranscendenceReceipts.Keys.First());
        p.TranscendenceReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};store.Save();return reply;
    }
}
