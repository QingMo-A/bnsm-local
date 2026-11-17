using System.Security.Cryptography;

internal sealed class LocalTrainingSlot
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public uint TrainingId {get;set;}
    public ulong CardSequence {get;set;}
    public uint CardLevel {get;set;}
    public ulong StartedAt {get;set;}
    public ulong EndTime {get;set;}
    public uint GoldCost {get;set;}
    public uint FastCashCost {get;set;}
    public uint RewardExp {get;set;}
    public bool Fast {get;set;}
    public uint HelpCount {get;set;}
    public ulong LastHelpAt {get;set;}
}

internal sealed partial class ProtocolRouter
{
    // The recovered CardTrainingDlg prefab has five slots, indexed 0..4.
    private const uint TrainingSlotCount=5;
    private const ulong TrainingHelpCooldown=600;
    private static bool InTraining(LocalPlayer p,ulong seq)=>seq!=0&&p.TrainingSlots.Values.Any(x=>x.CardSequence==seq);
    private uint OpenTrainingSlots(LocalPlayer p)=>(uint)Math.Clamp(1+catalog.VipFor(p.VipExp).TrainingBonusSlotCount,1,(int)TrainingSlotCount);
    private uint TrainingHelpLeft(LocalPlayer p)
    {
        ulong day=(Now+8*3600)/86400;
        uint used=p.TrainingHelpDay==day?p.TrainingHelpUsed:0,max=(uint)Math.Max(0,catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.TRAINING_CAMP_HELPER_MAX_COUNT));
        return max>used?max-used:0;
    }
    private PKCardTrainingSlot TrainingInfo(LocalPlayer p,uint index,LocalTrainingSlot? slot)
    {
        ulong seq=slot?.CardSequence??0;var state=seq!=0?CardState(p,seq):null;
        return new(){uid=p.Uid,userLevel=p.Level,nickName=p.Nickname,account=p.Account,slotIndex=index,
            trainingId=slot?.TrainingId??0,cardSeq=seq,cardId=seq!=0&&Owns(p,seq)?p.CardIds[(int)seq-1]:0,
            cardLevel=slot?.CardLevel??0,enchantGrade=state?.Enchant??0,endTime=slot?.EndTime??0,helpCount=slot?.HelpCount??0,
            helpCoolTimeEnd=slot?.LastHelpAt>0?slot.LastHelpAt+TrainingHelpCooldown:0,fastTraining=slot?.Fast??false,
            vipGrade=(uint)catalog.VipFor(p.VipExp).VipLv,avatarCardId=p.CardIds[(int)p.LeaderSequence-1]};
    }
    private Reply? HandleTraining(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!name.StartsWith("GS_CARD_TRAINING_",StringComparison.Ordinal))return null;
        bool mutation=name!="GS_CARD_TRAINING_LIST_REQUEST";
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(mutation&&p.TrainingReceipts.TryGetValue(key,out var prior))
            return prior.Fingerprint==fingerprint?new(prior.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name)
                :fail("training sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var reply=ExecuteTraining(name,request,p,ok,fail);
        if(mutation&&reply.Name.EndsWith("_OK",StringComparison.Ordinal)) {
            if(p.TrainingReceipts.Count>=256)p.TrainingReceipts.Remove(p.TrainingReceipts.Keys.First());
            p.TrainingReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};
            store.Save();
        }
        return reply;
    }
    private Reply ExecuteTraining(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_TRAINING_SLOT);
        bool open=FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CARD_TRAINING);
        if(name=="GS_CARD_TRAINING_LIST_REQUEST") {
            var list=new GsCardTrainingListOk{remainHelpCount=TrainingHelpLeft(p)};
            if(open||p.TrainingSlots.Count>0)for(uint i=0;i<TrainingSlotCount;i++) {
                p.TrainingSlots.TryGetValue(i,out var entry);
                if(i>=OpenTrainingSlots(p)&&entry==null)continue;
                if(entry?.CardSequence==0&&entry.EndTime<=Now)entry=null;
                list.trainingList.Add(TrainingInfo(p,i,entry));
            }
            // Local profiles on this server are the local-mode assistance contacts; no online users are invented.
            if(open)foreach(var friend in store.All().Where(x=>x.Uid!=p.Uid))foreach(var pair in friend.TrainingSlots.Where(x=>x.Key<TrainingSlotCount&&x.Value.CardSequence!=0&&Owns(friend,x.Value.CardSequence)))
                list.trainingList.Add(TrainingInfo(friend,pair.Key,pair.Value));
            list.castleBattleDefenceCardSeqList.AddRange(DeckSlots(p,55).Where(x=>x!=0));return ok(list);
        }
        if(!open)return fail("training unlocks at the original player level",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(name=="GS_CARD_TRAINING_START_REQUEST") {
            var r=(GsCardTrainingStartRequest)request!;
            if(r.slotIndex>=OpenTrainingSlots(p))return Invalid("training slot is VIP locked or invalid");
            p.TrainingSlots.TryGetValue(r.slotIndex,out var old);
            if(old!=null&&(old.CardSequence!=0||old.EndTime>Now))return Invalid("training slot is occupied or cooling down");
            var rule=catalog.Training.FirstOrDefault(x=>x.Id==r.trainId);
            if(rule==null||rule.TrainTime<=0)return fail("unknown original training plan",ServerErrorCode.ERROR_INVALID_TRAINING_ID);
            if(!Owns(p,r.targetCardSeq))return fail("training card is not owned",ServerErrorCode.ERROR_INVALID_CARD);
            if(InTraining(p,r.targetCardSeq))return fail("card is already training",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            if(p.ActiveStage!=null)return fail("finish the current battle before training",ServerErrorCode.ERROR_INVALID_CARD);
            if(DeckSlots(p,8).Contains(r.targetCardSeq)||DeckSlots(p,55).Contains(r.targetCardSeq))return fail("remove this card from defence decks before training",ServerErrorCode.ERROR_CANNOT_USE_TRAINING_CARD);
            var data=catalog.RequireCard(p.CardIds[(int)r.targetCardSeq-1]);var card=CardState(p,r.targetCardSeq);
            int max=ProgressionRules.CardMax(catalog,p,data);
            if(card.Level>=max)return fail("card is already at its current level cap",ServerErrorCode.ERROR_CARD_LEVEL_IS_OVER_LIMIT);
            var exp=catalog.TrainingExp.FirstOrDefault(x=>x.CardLevel==card.Level);int index=exp?.TrainingId.IndexOf((int)r.trainId)??-1;
            if(exp==null||index<0||index>=exp.ExpValue.Count||index>=exp.ConsumeTrainGold.Count||index>=exp.ConsumeFastTrainCash.Count||exp.ExpValue[index]<=0||exp.ConsumeTrainGold[index]<0||exp.ConsumeFastTrainCash[index]<0)
                return fail("training level rule is unavailable",ServerErrorCode.ERROR_INVALID_TRAINING_ID);
            uint gold=(uint)exp.ConsumeTrainGold[index];if(p.Gold<gold)return fail("not enough training gold",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
            p.Gold-=gold;p.TrainingSlots[r.slotIndex]=new(){TrainingId=r.trainId,CardSequence=r.targetCardSeq,CardLevel=card.Level,StartedAt=Now,EndTime=Now+(ulong)rule.TrainTime*3600,
                GoldCost=gold,FastCashCost=(uint)exp.ConsumeFastTrainCash[index],RewardExp=(uint)exp.ExpValue[index]};
            foreach(var deck in p.Decks.Where(x=>x.Key!=10).Select(x=>x.Value).Append(p.Deck))for(int i=0;i<deck.Count;i++)if(deck[i]==r.targetCardSeq)deck[i]=0;
            return ok(new GSCardTrainingStartOk{resultCard=CardInfo((uint)data.Id,r.targetCardSeq,p),nowCoinInfo=Coins(p)});
        }
        if(name=="GS_CARD_TRAINING_HELP_FRIEND_REQUEST") {
            var r=(GsCardTrainingHelpFriendRequest)request!;var target=store.Find(r.friendUid);
            if(target==null||target.Uid==p.Uid||r.slotIndex>=TrainingSlotCount||!target.TrainingSlots.TryGetValue(r.slotIndex,out var t)||t.CardSequence==0||!Owns(target,t.CardSequence)||t.Fast||t.EndTime<=Now+TrainingHelpCooldown)
                return Invalid("no active friend's training eligible for help");
            if(TrainingHelpLeft(p)==0)return fail("daily training assistance limit",ServerErrorCode.ERROR_TRAINING_GIVE_HELP_COUNT_MAX);
            uint max=(uint)Math.Max(0,catalog.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.TRAINING_CAMP_HELP_MAX_COUNT));
            if(t.HelpCount>=max)return fail("training help count is full",ServerErrorCode.ERROR_TRAINING_CARD_HELP_COUNT_MAX);
            if(t.LastHelpAt!=0&&t.LastHelpAt+TrainingHelpCooldown>Now)return fail("training help is cooling down",ServerErrorCode.ERROR_TRAINING_CARD_HELP_COOLTIME);
            var rule=catalog.Training.First(x=>x.Id==t.TrainingId);
            bool reward=rule.FriendRewardQuantity>0&&Random.Shared.Next(100)<Math.Clamp(rule.FriendRewardProb,0,100);
            uint id=(uint)Math.Max(0,rule.FriendRewardId);
            if(reward&&(!catalog.Items.Any(x=>x.Id==id)||(long)p.Inventory.GetValueOrDefault(id)+rule.FriendRewardQuantity>int.MaxValue))return fail("helper reward inventory capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            ulong day=(Now+8*3600)/86400;if(p.TrainingHelpDay!=day){p.TrainingHelpDay=day;p.TrainingHelpUsed=0;}
            p.TrainingHelpUsed++;t.HelpCount++;t.LastHelpAt=Now;
            var result=new GsCardTrainingHelpFriendOk{resultCardInfo=TrainingInfo(target,r.slotIndex,t),rewardItem=new PKUserItem(),remainCount=TrainingHelpLeft(p)};
            if(reward){p.Inventory[id]=p.Inventory.GetValueOrDefault(id)+rule.FriendRewardQuantity;result.rewardItem=ItemInfo(id,rule.FriendRewardQuantity);}
            return ok(result);
        }
        uint slot=name=="GS_CARD_TRAINING_FAST_REQUEST"?((GsCardTrainingFastRequest)request!).slotIndex:((GSCardTrainingEndRequest)request!).slotIndex;
        if(slot>=TrainingSlotCount||!p.TrainingSlots.TryGetValue(slot,out var training)||training.CardSequence==0||!Owns(p,training.CardSequence))return Invalid("training slot is empty or invalid");
        if(name=="GS_CARD_TRAINING_FAST_REQUEST") {
            if(training.Fast||training.EndTime<=Now)return Invalid("training is already complete");
            if(p.CashPoint<training.FastCashCost)return fail("not enough acceleration diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=training.FastCashCost;training.Fast=true;return ok(new GsCardTrainingFastOk{coinInfo=Coins(p)});
        }
        if(name!="GS_CARD_TRAINING_END_REQUEST")return Invalid("unknown training request");
        var end=((GSCardTrainingEndRequest)request!).endType;ulong seq=training.CardSequence;
        if(!Enum.IsDefined(end))return Invalid("unknown training end type");
        if(p.ActiveStage!=null)return fail("finish the current battle before settling training",ServerErrorCode.ERROR_INVALID_CARD);
        if(end==CardTrainingEndType.TRAINING_END_GIVEUP) {
            if(training.Fast||training.EndTime<=Now)return Invalid("completed training must be claimed");
            if((ulong)p.Gold+training.GoldCost>uint.MaxValue)return fail("refund exceeds wallet capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            p.Gold+=training.GoldCost;p.TrainingSlots.Remove(slot);
        } else {
            if(end==CardTrainingEndType.TRAINING_END_FAST?!training.Fast:training.Fast||training.EndTime>Now)
                return fail("training has not reached the requested completion state",ServerErrorCode.ERROR_TRAINING_TIME_NOT_YET);
            var card=CardState(p,seq);var data=catalog.RequireCard(p.CardIds[(int)seq-1]);int max=ProgressionRules.CardMax(catalog,p,data);
            var rule=catalog.Training.First(x=>x.Id==training.TrainingId);
            // The server's original bonus algorithm is unavailable: local assistance adds FriendRewardExp percent per help.
            ulong gain=(ulong)training.RewardExp+(ulong)training.RewardExp*training.HelpCount*(uint)Math.Max(0,rule.FriendRewardExp)/100;
            uint ceiling=ProgressionRules.CardCeiling(catalog,max);
            card.Exp=(uint)Math.Max(card.Exp,Math.Min((ulong)ceiling,(ulong)card.Exp+gain));
            card.Level=Math.Max(card.Level,ProgressionRules.CardLevel(catalog,card.Exp,max));
            if(training.Fast&&training.EndTime>Now){training.CardSequence=0;training.Fast=false;training.HelpCount=0;training.LastHelpAt=0;}
            else p.TrainingSlots.Remove(slot);
        }
        return ok(new GsCardTrainingEndOk{resultCard=CardInfo(p.CardIds[(int)seq-1],seq,p),nowCoinInfo=Coins(p)});
    }
}
