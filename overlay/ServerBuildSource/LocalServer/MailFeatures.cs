using System.Text.Json;

internal sealed class LocalMailReward
{
    public uint Type {get;set;}
    public int Id {get;set;}
    public int Count {get;set;}
}
internal sealed class LocalMail
{
    public ulong Sequence {get;set;}
    public ulong Time {get;set;}
    public uint Kind {get;set;}
    public uint Rank {get;set;}
    public uint Territory {get;set;}
    public string Message {get;set;}="";
    public List<LocalMailReward> Rewards {get;set;}=[];
}
internal sealed class LocalRankSnapshot
{
    public ulong Time {get;set;}
    public uint Rank {get;set;}
}
internal sealed class LocalMailbox
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong NextSequence {get;set;}=1;
    public List<LocalMail> Pending {get;set;}=[];
    public bool ArenaClockInitialized {get;set;}
    public ulong ArenaCutoff {get;set;}
    public List<LocalRankSnapshot> ArenaRanks {get;set;}=[];
    public ulong InfiniteArenaCutoff {get;set;}
    public List<LocalRankSnapshot> InfiniteArenaRanks {get;set;}=[];
    public Dictionary<uint,ulong> WorldDailyPaid {get;set;}=[];
    public Dictionary<uint,ulong> WorldWeeklyPaid {get;set;}=[];
    public Dictionary<string,LocalMutationReceipt> Receipts {get;set;}=[];
}
internal sealed partial class ProtocolRouter
{
    // Native ArenaRuleDialog states 22:00 at server UTC+8. Settlement has its own
    // persisted clock; daily ticket resets and polling cannot award it twice.
    internal static ulong ArenaRewardCutoff(ulong now)
    {
        ulong local=now+8*3600,day=local/86400,cutoff=day*86400+22*3600;
        if(local<cutoff)cutoff-=86400;
        return cutoff-8*3600;
    }
    internal void SettlementTick(ulong? at=null)
    {
        lock(store.Gate){using var batch=store.BatchSaves();ulong now=at??Now,target=ArenaRewardCutoff(now);
            var players=store.All();bool changed=SettleWorldWarMail(players,now);
            foreach(var p in players){
                var m=p.Mailbox;
                changed|=SettleInfiniteArenaMail(p,target);
                if(!p.Competitive.Records.TryGetValue(12,out var r)||r.Battles==0)continue;
                if(!m.ArenaClockInitialized){
                    m.ArenaClockInitialized=true;m.ArenaCutoff=target-86400;
                    if(m.ArenaRanks.Count==0)m.ArenaRanks.Add(new(){Time=r.Latest==0?now:r.Latest,Rank=r.Rank});
                    changed=true;
                }
                for(int i=0;i<31&&m.ArenaCutoff<target&&m.Pending.Count<500;i++){
                    ulong cutoff=m.ArenaCutoff+86400;
                    var snapshot=m.ArenaRanks.LastOrDefault(x=>x.Time<=cutoff);
                    if(snapshot!=null){
                        var reward=Competitive.Rewards.Where(x=>(int)x.PvpBattleType==3&&(int)x.BattleResultType==4&&x.Ranking_Min<=snapshot.Rank&&(x.Ranking_Max==0||snapshot.Rank<=x.Ranking_Max)).OrderByDescending(x=>x.Ranking_Min).FirstOrDefault();
                        if(reward==null)break; // Do not consume a settlement with missing catalog data.
                        var mail=new LocalMail{Sequence=m.NextSequence++,Time=cutoff,Kind=22,Rank=snapshot.Rank,Message="天下争霸每日排名结算（服务器时间22:00）"};
                        for(int j=0;j<reward.ItemQuantity.Count;j++)if(reward.ItemQuantity[j]>0)mail.Rewards.Add(new(){Type=(uint)reward.RewardValueIdType[j],Id=reward.RewardValueId[j],Count=reward.ItemQuantity[j]});
                        m.Pending.Add(mail);
                        System.Console.WriteLine($"[mail] uid={p.Uid} arena cutoff={cutoff} rank={snapshot.Rank} seq={mail.Sequence}");
                    }
                    m.ArenaCutoff=cutoff;changed=true;
                }
                // Retain the last snapshot at/before the processed cutoff plus
                // later changes for offline catch-up across multiple midnights.
                var anchor=m.ArenaRanks.LastOrDefault(x=>x.Time<=m.ArenaCutoff);
                if(anchor!=null)m.ArenaRanks.RemoveAll(x=>x.Time<anchor.Time);
            }
            if(changed)store.Save();
        }
    }
    private bool SettleInfiniteArenaMail(LocalPlayer p,ulong target)
    {
        if(!p.Competitive.Records.TryGetValue(31,out var record)||record.Battles==0)return false;
        var mailbox=p.Mailbox;
        var ranks=mailbox.InfiniteArenaRanks;
        bool changed=false;
        if(mailbox.InfiniteArenaCutoff==0)
        {
            mailbox.InfiniteArenaCutoff=target-86400;
            if(ranks.Count==0&&record.Latest>0)ranks.Add(new(){Time=record.Latest,Rank=record.Rank});
            changed=true;
        }
        for(int i=0;i<31&&mailbox.InfiniteArenaCutoff<target&&mailbox.Pending.Count<500;i++)
        {
            ulong cutoff=mailbox.InfiniteArenaCutoff+86400;
            var snapshot=ranks.Where(x=>x.Time<=cutoff).OrderBy(x=>x.Time).LastOrDefault();
            if(snapshot!=null)
            {
                var reward=Competitive.Rewards.FirstOrDefault(x=>x.PvpBattleType==PvpType(31)
                    &&x.BattleResultType==BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_RANKING
                    &&x.Ranking_Min<=snapshot.Rank&&snapshot.Rank<=x.Ranking_Max);
                if(reward==null)break;
                var mail=new LocalMail{Sequence=mailbox.NextSequence++,Time=cutoff,
                    Kind=(uint)RewardGiveType.REWARD_GIVE_TYPE_INFINITE_ARENA_BATTLE_RANK,Rank=snapshot.Rank,Message="斗神争霸每日排名结算（服务器时间22:00）"};
                for(int j=0;j<reward.ItemQuantity.Count;j++)
                    if(reward.ItemQuantity[j]>0)mail.Rewards.Add(new(){Type=(uint)reward.RewardValueIdType[j],Id=reward.RewardValueId[j],Count=reward.ItemQuantity[j]});
                mailbox.Pending.Add(mail);
            }
            mailbox.InfiniteArenaCutoff=cutoff;
            changed=true;
        }
        var anchor=ranks.Where(x=>x.Time<=mailbox.InfiniteArenaCutoff).OrderBy(x=>x.Time).LastOrDefault();
        if(anchor!=null)changed|=ranks.RemoveAll(x=>x!=anchor&&x.Time<=anchor.Time)>0;
        return changed;
    }
    private Reply? HandleMailbox(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_GET_REWARD_LIST_REQUEST"){
            ulong latest=((GsGetRewardInfoRequest)request!).latestRewardSeq;var result=new GsGetRewardInfoOk();
            // LocalRuntime sends bounded parts using the native totalDataCount contract.
            foreach(var mail in p.Mailbox.Pending.Where(x=>x.Sequence>latest).OrderBy(x=>x.Sequence)){
                var row=new PKRewardInfo{rewardSeq=mail.Sequence,rewardTime=mail.Time,rewardType=(RewardGiveType)mail.Kind,rewardID1=mail.Territory==0?mail.Rank:mail.Territory,rewardID2=mail.Territory==0?0:mail.Rank,rewardMessage=mail.Message};
                row.rewardItemInfo.AddRange(mail.Rewards.Select(x=>new PKRewardItemInfo{rewardItemType=(BscTable.REWARD_TYPE)x.Type,rewardItemData=(ulong)x.Id,rewardItemCount=(uint)x.Count}));result.rewardList.Add(row);
            }
            result.totalDataCount=result.dataCount=(uint)result.rewardList.Count;return ok(result);
        }
        if(name!="GS_RECEIVE_REWARD_REQUEST")return null;
        return FeatureReceipt(p,p.Mailbox.Receipts,name,session,payload,()=>{
            ulong seq=((GsReceiveRewardItemDataRequest)request!).rewardSeq;
            // Claim-all is deliberately bounded; remaining mail stays visible.
            var selected=p.Mailbox.Pending.Where(x=>seq==0||x.Sequence==seq).OrderBy(x=>x.Sequence).Take(10).ToList();
            if(selected.Count==0)return fail("mail was already claimed or no longer exists",ServerErrorCode.ERROR_INVALID_GIFT_BOX_REWARD_INFO);
            var grant=new ShopGrant();ulong arena=0,rank=0;
            foreach(var reward in selected.SelectMany(x=>x.Rewards)){
                var type=(BscTable.REWARD_TYPE)reward.Type;
                if(type==BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT)arena+=(uint)reward.Count;
                else if(type==BscTable.REWARD_TYPE.REWARD_TYPE_RANK_POINT)rank+=(uint)reward.Count;
                else if(!AddShopReward(p,grant,type,reward.Id,reward.Count))return fail("unsupported mail attachment",ServerErrorCode.ERROR_INVALID_ID);
            }
            if((ulong)p.ArenaPoint+arena>uint.MaxValue||(ulong)p.RankingPoint+rank>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("mail attachments exceed currency or storage capacity; free space and retry",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var delta=new PKReceiveRewardItemInfo{rewardSeq=selected[0].Sequence,addGold=(uint)grant.Gold,addCashPoint=(uint)grant.Cash,addStamina=(uint)grant.Stamina,addArenaPoint=(uint)arena,addRankPoint=(uint)rank,addGuildToken=(uint)grant.Tokens};
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,delta.addItemList,delta.addCardInfoList);p.ArenaPoint+=(uint)arena;p.RankingPoint+=(uint)rank;
            var reply=new GsReceiveRewardItemDataOk{currentCoinInfo=Coins(p),stamina=p.Stamina,staminaStamp=p.StaminaStamp};reply.rewardData.Add(delta);
            foreach(var mail in selected.Skip(1))reply.rewardData.Add(new(){rewardSeq=mail.Sequence});
            p.Mailbox.Pending.RemoveAll(x=>selected.Contains(x));reply.totalDataCount=reply.dataCount=(uint)reply.rewardData.Count;return ok(reply);
        },fail);
    }
    private PKCheckGameEntryData MailEntry(LocalPlayer p)
    {
        var data=RaidEntry(p);data.currentRewardCount=data.newRewardCount=(uint)p.Mailbox.Pending.Count;return data;
    }
}
