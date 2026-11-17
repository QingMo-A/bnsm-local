using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    // The client supplies no helper ID: gifts go to the currently selected helper.
    private Reply? HandleHelperGifts(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,
        Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name!="GS_GIVE_ITEM_TO_HELPER_REQUEST")return null;
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.HelperReceipts.TryGetValue(key,out var prior))
            return prior.Fingerprint==fingerprint?new(prior.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name)
                :fail("helper sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var rule=catalog.HelperRewards.FirstOrDefault();
        if(rule==null || rule.ItemId<=0 || rule.GetMin<=0 || rule.GetMax<rule.GetMin)
            return fail("helper reward configuration unavailable",ServerErrorCode.ERROR_INVALID_ID);
        uint count=((GsGiveItemToHelperRequest)request!).itemCount;
        var response=new GsGiveItemToHelperOk {accumulation=p.HelperLikeAccumulation,coinInfo=Coins(p),earnedCoinInfo=new PKCoinInfo()};
        // Zero is the original dialog's read-only initialization request.
        if(count==0)return ok(response);
        if(!catalog.Helpers.Any(x=>(uint)x.HelperType==p.HelperId) || p.Level<(uint)catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_HELPER_REQUEST))
            return fail("helper gifts are not unlocked",ServerErrorCode.ERROR_INVALID_ID);
        if(count>1000 || p.Inventory.GetValueOrDefault((uint)rule.ItemId)<count)
            return fail("not enough helper gift items or batch too large",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        var pools=rule.RewardPoolList.Where(x=>x.PoolProb>0 && p.Level>=x.PCLevelMin && p.Level<=x.PCLevelMax).ToList();
        if(pools.Count==0)return fail("no helper reward for this level",ServerErrorCode.ERROR_INVALID_ID);
        T Pick<T>(IReadOnlyList<T> values,Func<T,int> weight) {
            long total=values.Sum(x=>(long)Math.Max(0,weight(x)));
            if(total<=0)throw new InvalidDataException("Empty helper reward weights");
            long roll=Random.Shared.NextInt64(total);
            foreach(var value in values) {roll-=Math.Max(0,weight(value));if(roll<0)return value;}
            throw new InvalidOperationException();
        }
        uint Threshold()=>(uint)Random.Shared.Next(rule.GetMin,checked(rule.GetMax+1));
        // Original server threshold selection is unavailable. Local policy uses
        // the recovered GetMin/GetMax as an inclusive, persisted gift threshold.
        uint threshold=p.HelperLikeRewardAt;
        if(threshold<rule.GetMin || threshold>rule.GetMax)threshold=Threshold();
        ulong accumulation=(ulong)p.HelperLikeAccumulation+count;
        var grant=new ShopGrant();
        while(accumulation>=threshold) {
            accumulation-=threshold;
            var pool=Pick(pools,x=>x.PoolProb);response.rewardPoolId=(uint)pool.PoolId;
            var rewards=pool.RewardList.Where(x=>x.Prob>0).ToList();
            if(rewards.Count==0 || pool.RewardCount<1 || pool.RewardCount>100)
                return fail("invalid helper reward pool",ServerErrorCode.ERROR_INVALID_ID);
            for(int i=0;i<pool.RewardCount;i++) {
                var reward=Pick(rewards,x=>x.Prob);
                if(!AddShopReward(p,grant,reward.RewardIdType,reward.RewardId,reward.Quantity))
                    return fail("helper reward cannot be granted",ServerErrorCode.ERROR_INVALID_ID);
            }
            threshold=Threshold();
        }
        if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))
            return fail("helper reward capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        p.Inventory[(uint)rule.ItemId]-=(int)count;
        ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,response.changedItemList,response.earnedCardList);
        response.changedItemList.Insert(0,ItemInfo((uint)rule.ItemId,-(int)count));
        p.HelperLikeAccumulation=(uint)accumulation;p.HelperLikeRewardAt=threshold;
        response.accumulation=p.HelperLikeAccumulation;response.coinInfo=Coins(p);
        response.earnedCoinInfo.gold=(uint)grant.Gold;response.earnedCoinInfo.cashPoint=(uint)grant.Cash;
        var result=ok(response);
        if(p.HelperReceipts.Count>=128)p.HelperReceipts.Remove(p.HelperReceipts.Keys.First());
        p.HelperReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=response.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(response))};
        store.Save();return result;
    }
}
