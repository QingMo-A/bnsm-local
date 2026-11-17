using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    private Reply? HandleLoot(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_OPEN_INVENTORY_BOX_ITEM_REQUEST" or "GS_NORMAL_BATTLE_SKIP_REQUEST" or "GS_NORMAL_BATTLE_END_CONFIRM_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.LootReceipts.TryGetValue(key,out var prior))
        {
            if (prior.Fingerprint != fingerprint)
                return fail("loot sequence reused with different parameters", ServerErrorCode.ERROR_INVALID_ID);
            if (request is GsNormalBattleEndConfirmRequest end && LocalStoryChapter.Find((int)end.stageId) != null &&
                p.ActiveBattleStamp != 0 && p.ActiveBattleStamp != end.timeStamp)
                return fail("story settlement belongs to an earlier battle", ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            return new(prior.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name);
        }
        var result=request switch {
            GsOpenInventoryBoxItemRequest r=>OpenInventoryBox(p,r,ok,fail),
            GsNormalBattleSkipRequest r => LocalStoryChapter.Find((int)r.stageId) is { } chapter
                ? SweepStory(p, r, chapter, ok, fail)
                : SkipStage(p, r, ok, fail),
            GsNormalBattleEndConfirmRequest r=>SettleStageLoot(p,r,ok,fail),
            _=>fail("invalid reward request",ServerErrorCode.ERROR_INVALID_ID)
        };
        if(result.Name.EndsWith("_OK")) {
            if(p.LootReceipts.Count>=256)p.LootReceipts.Remove(p.LootReceipts.Keys.First());
            p.LootReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        return result;
    }
    private static int LootRange(int min,int max)
    {
        if(min<0||max<min)throw new InvalidDataException("Invalid original reward range");
        return min==max?min:(int)RandomNumberGenerator.GetInt32(min,max==int.MaxValue?max:max+1);
    }
    internal sealed record BoxChoice(bool Card,int Id,int Count,int Weight);
    internal static List<BoxChoice>[] BoxColumns(BscTable.BoxData box)
    {
        var columns=Enumerable.Range(0,Math.Max(1,box.UseRewardSlot)).Select(_=>new List<BoxChoice>()).ToArray();
        foreach(var row in box.BoxDataInfoList) {
            int[] cards=[row.Card1Id,row.Card2Id,row.Card3Id,row.Card4Id],cardWeights=[row.Card1Prob,row.Card2Prob,row.Card3Prob,row.Card4Prob];
            int[] items=[row.Item1Id,row.Item2Id,row.Item3Id,row.Item4Id],counts=[row.Item1Quantity,row.Item2Quantity,row.Item3Quantity,row.Item4Quantity],weights=[row.Item1Prob,row.Item2Prob,row.Item3Prob,row.Item4Prob];
            for(int i=0;i<Math.Min(4,columns.Length);i++) {
                if(cards[i]>0)columns[i].Add(new(true,cards[i],1,cardWeights[i]));
                if(items[i]>0&&counts[i]>0)columns[i].Add(new(false,items[i],counts[i],weights[i]));
            }
            // Some APK schemas also have repeated forms. Prefer the populated
            // representation, never count both encodings of the same reward.
            if(cards.All(x=>x==0)&&items.All(x=>x==0)) {
                for(int i=0;i<row.CardId.Count;i++)columns[0].Add(new(true,row.CardId[i],1,row.CardProb.ElementAtOrDefault(i)));
                for(int i=0;i<row.ItemId.Count;i++)columns[0].Add(new(false,row.ItemId[i],row.ItemQuantity.ElementAtOrDefault(i),row.ItemProb.ElementAtOrDefault(i)));
            }
        }
        return columns;
    }
    private static T WeightedLoot<T>(IReadOnlyList<T> choices,Func<T,int> weight)
    {
        int total=choices.Sum(x=>Math.Max(0,weight(x)));
        if(total<=0)throw new InvalidDataException("Reward weights are absent");
        int roll=RandomNumberGenerator.GetInt32(total);
        foreach(var choice in choices) {roll-=Math.Max(0,weight(choice));if(roll<0)return choice;}
        throw new InvalidDataException("Invalid reward weights");
    }
    private Reply OpenInventoryBox(LocalPlayer p,GsOpenInventoryBoxItemRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);
        if(p.ActiveStage!=null)return fail("finish the current battle before opening boxes",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        var box=catalog.Boxes.FirstOrDefault(x=>x.Id==r.boxItemId);
        if(box==null||!catalog.Items.Any(x=>x.Id==r.boxItemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_BOX)||r.openCount is 0 or >10)return Invalid("invalid original box or open count (1..10)");
        if(p.Inventory.GetValueOrDefault(r.boxItemId)<r.openCount)return fail("not enough boxes",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        if(box.UnlockId!=0&&p.Inventory.GetValueOrDefault((uint)box.UnlockId)<r.openCount)return fail("not enough matching box keys",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        var grant=new ShopGrant();ulong moonstone=0;
        try {
            var columns=BoxColumns(box);
            if(box.UseRewardSlot is <1 or >4)return Invalid("unsupported original box slot layout");
            for(uint n=0;n<r.openCount;n++) {
                grant.Gold+=(uint)LootRange(box.GoldMin,box.GoldMax);grant.Cash+=(uint)LootRange(box.CashMin,box.CashMax);moonstone+=(uint)LootRange(box.MoonstoneMin,box.MoonstoneMax);
                foreach(var column in columns) {
                    if(column.Count==0)continue; // Currency-only slots are described by their min/max fields.
                    int draws=columns.Length==1?Math.Max(1,LootRange(box.CardMax>0?box.CardMin:box.ItemMin,box.CardMax>0?box.CardMax:box.ItemMax)):1;
                    for(int i=0;i<draws;i++) {
                        var choices=box.GiveRandom?new[]{WeightedLoot(column,x=>x.Weight)}:column.ToArray();
                        foreach(var choice in choices)
                            if(!AddShopReward(p,grant,choice.Card?BscTable.REWARD_TYPE.REWARD_TYPE_CARD:BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,choice.Id,choice.Count))return Invalid("box contains an unavailable original reward; nothing was consumed");
                        if(!box.GiveRandom)break;
                    }
                }
            }
        } catch(InvalidDataException e) {return Invalid(e.Message);}
        int slots=grant.Items.Where(x=>catalog.Items.First(i=>i.Id==x.Key).ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).Sum(x=>x.Value);
        if((ulong)p.Moonstone+moonstone>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("box rewards exceed storage capacity; nothing was consumed",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsOpenInventoryBoxItemOk {addCoinInfo=new() {gold=(uint)grant.Gold,cashPoint=(uint)grant.Cash},addMoonStone=(uint)moonstone};
        ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.deltaItemInfoList,result.addCardInfoList);
        void Consume(uint id) {int left=p.Inventory[id]-(int)r.openCount;if(left==0)p.Inventory.Remove(id);else p.Inventory[id]=left;result.deltaItemInfoList.Add(ItemInfo(id,-(int)r.openCount));}
        Consume(r.boxItemId);if(box.UnlockId!=0)Consume((uint)box.UnlockId);
        p.Moonstone+=(uint)moonstone;result.currentMoonStone=p.Moonstone;result.currentCoinInfo=Coins(p);return ok(result);
    }

    private bool AddStageRow(LocalPlayer p,ShopGrant grant,BscTable.NPC_REWARD_TYPE type,int id,int count,HashSet<int>? path=null)
    {
        if(count<=0)return false;
        if(type==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM)
            return AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,id,count);
        if(type==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_CARD)return AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_CARD,id,count);
        if(type!=BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM)return false;
        path??=[];if(path.Count>=16||!path.Add(id))return false;
        try {
            var group=catalog.DropGroups.FirstOrDefault(x=>x.Id==id);
            if(group==null||group.NpcRewardId.Count==0||group.NpcRewardId.Count!=group.Quantity.Count||group.NpcRewardId.Count!=group.NpcRewardIdType.Count)return false;
            // The supplied CDT omits group probabilities too. Local policy:
            // one equally weighted listed outcome per group draw when absent.
            for(int n=0;n<count;n++) {
                int i=group.DropProb.Count==group.NpcRewardId.Count
                    ?WeightedLoot(Enumerable.Range(0,group.NpcRewardId.Count).ToArray(),x=>(int)Math.Round(group.DropProb[x]*10000))
                    :RandomNumberGenerator.GetInt32(group.NpcRewardId.Count);
                if(!AddStageRow(p,grant,group.NpcRewardIdType[i],group.NpcRewardId[i],group.Quantity[i],path))return false;
            }
            return true;
        } finally {path.Remove(id);}
    }
    private bool PlanStageLoot(LocalPlayer p,BscTable.BattleStageLink stage,bool first,bool adventure,int multiplier,ShopGrant grant)
    {
        var rows=new Dictionary<(BscTable.NPC_REWARD_TYPE,int),int>();
        bool Rows(List<BscTable.NPC_REWARD_TYPE> types,List<int> ids,List<int> quantities,List<double> probs,bool firstClear) {
            if(types.Count!=ids.Count||ids.Count!=quantities.Count||probs.Count!=0&&probs.Count!=ids.Count)return false;
            for(int i=0;i<ids.Count;i++) {
                double chance=100;
                if(probs.Count!=0)chance=probs[i];
                else if(adventure&&!firstClear) {
                    var itemType=types[i]==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM&&catalog.ItemsById.TryGetValue(ids[i],out var item)
                        ?item.ItemType:BscTable.ITEM_TYPE.ITEM_TYPE_NONE;
                    chance=LocalAdventureDropRules.BaseChance(types[i],itemType);
                }
                if(double.IsNaN(chance)||double.IsInfinity(chance)||chance<0)return false;
                if(!firstClear)chance=LocalAdventureDropRules.EffectiveChance(chance,multiplier);
                if(chance==0||chance<100&&Random.Shared.NextDouble()*100>=chance)continue;

                var key=(types[i],ids[i]);
                rows[key]=firstClear?Math.Max(rows.GetValueOrDefault(key),quantities[i]):rows.GetValueOrDefault(key)+quantities[i];
            }
            return true;
        }
        if(!Rows(stage.NpcRewardIdType,stage.NpcRewardId,stage.RewardQuantity,stage.DropProb,false))return false;
        if(first&&!Rows(stage.FirstNpcRewardIdType,stage.FirstNpcRewardId,stage.FirstRewardQuantity,stage.FirstDropProb,true))return false;
        foreach(var (key,count) in rows)if(!AddStageRow(p,grant,key.Item1,key.Item2,count))return false;
        grant.Gold+=(uint)Math.Max(0,stage.Gold);return true;
    }
    private (uint Exp,uint Level,uint Stamina)? StageGrowth(LocalPlayer p,ulong earnedExp,uint spendStamina)
    {
        ulong exp=(ulong)Math.Max(p.Exp,ProgressionRules.PlayerFloor(catalog,p.Level))+earnedExp;if(exp>uint.MaxValue)return null;
        uint cap=ProgressionRules.PlayerMax(catalog);
        uint level=Math.Max(p.Level,(uint)catalog.PlayerStates.Where(x=>(ulong)x.Exp<=exp && x.Level<=cap).Max(x=>x.Level));
        long stamina=(long)p.Stamina-spendStamina+catalog.PlayerStates.Where(x=>x.Level>p.Level&&x.Level<=level).Sum(x=>(long)Math.Max(0,x.LevelUpStaminaReward));
        return stamina is <0 or >uint.MaxValue?null:((uint)exp,level,(uint)stamina);
    }
    private Reply SettleStageLoot(LocalPlayer p,GsNormalBattleEndConfirmRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (LocalStoryChapter.Find((int)r.stageId) is { } chapter) return SettleStory(p,r,chapter,ok,fail);
        if(p.ActiveStage!=r.stageId||p.ActiveDifficulty!=r.difficulty||r.dungeonFlag>1||p.ActiveDungeon!=(r.dungeonFlag==1)||
            (r.timeStamp!=0&&r.timeStamp!=p.ActiveBattleStamp)||!Enum.IsDefined(r.endType)||!catalog.BattleStages(r.difficulty,p.ActiveDungeon).TryGetValue(r.stageId,out var stage))return Invalid("no matching active battle to settle");
        bool won=r.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS,dungeon=p.ActiveDungeon;
        var grant=new ShopGrant();uint earnedExp=won?(uint)Math.Max(0,stage.RewardExp):0,growth=won?(uint)Math.Max(0,stage.CardRewardExp):0;
        var records=dungeon?DungeonRecordsOf(p,r.difficulty):StageRecordsOf(p,r.difficulty);
        bool first=records.GetValueOrDefault(r.stageId)?.Stars is not >0&&(dungeon||StageUnlock.ClearStageOf(p,r.difficulty)<r.stageId);
        bool adventure=r.stageId>GameCatalog.TutorialEndStage||dungeon;
        int multiplier=adventure?Attendance.AdventureDropChanceMultiplier(r.difficulty,dungeon,Now):1;
        if(won&&(!PlanStageLoot(p,stage,first,adventure,multiplier,grant)||(growth>0&&!AddShopReward(p,grant,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,(int)GrowupItem,(int)growth))))return Invalid("original stage reward is unavailable");
        var progress=StageGrowth(p,earnedExp,0);
        if(progress==null||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("battle reward storage limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsNormalBattleEndConfirmOk {earnedExp=earnedExp,earnedCoinInfo=new() {gold=(uint)grant.Gold,cashPoint=(uint)grant.Cash}};
        ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);
        (p.Exp,p.Level,p.Stamina)=progress.Value;
        if(p.Stamina>=catalog.StaminaMax(p.Level))p.StaminaStamp=Now;
        if(won) {if(!dungeon)StageUnlock.SetClearStage(p,r.difficulty,r.stageId);RecordStageResult(p,r.difficulty,r.stageId,r.cardDieCount,r.battleEndSyncData?.clearTime??0,dungeon);RecordObjective(p,BscTable.CHECK_TYPE.MCT_CLEAR_PVE);}
        LocalBattleAttempts.Clear(p);
        result.exp=p.Exp;result.level=p.Level;result.clearStageID=StageUnlock.ClearStageOf(p,r.difficulty);result.coinInfo=Coins(p);result.stamina=p.Stamina;result.staminaMax=catalog.StaminaMax(p.Level);result.staminaStamp=p.StaminaStamp;
        return ok(result);
    }
    private Reply SkipStage(LocalPlayer p,GsNormalBattleSkipRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if(!Enum.IsDefined(r.difficulty)||r.difficulty==BscTable.GAME_DIFFICULTY.DIFFICULTY_MAX||!catalog.BattleStages(r.difficulty,r.dungeonFlag).TryGetValue(r.stageId,out var stage))return Invalid("unknown sweep stage or difficulty");
        int max=(int)catalog.Numbers.GetValueOrDefault(r.difficulty==BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL?BscTable.NUMBER_TYPE.SKIP_BATTLE_COUNT_NORMAL:BscTable.NUMBER_TYPE.SKIP_BATTLE_COUNT_HARD);
        if(p.ActiveStage!=null||r.skipCount==0||r.skipCount>max||(r.skipCount>1&&!catalog.VipFor(p.VipExp).ChainBattleSkip)||r.useCash)return Invalid("invalid sweep count, active battle or unsupported cash mode");
        var records=r.dungeonFlag?DungeonRecordsOf(p,r.difficulty):StageRecordsOf(p,r.difficulty);
        if((records.GetValueOrDefault(r.stageId)?.Stars??0)<3||(r.dungeonFlag?!CanEnterDungeon(p,r.difficulty,stage):!StageUnlock.CanEnter(catalog,p,r.difficulty,r.stageId))||p.Level<stage.RequirePcLevel)return Invalid("sweep requires an unlocked three-star stage");
        if(RemainingBattleCount(p,stage,r.difficulty,r.dungeonFlag)<r.skipCount)return fail("not enough remaining daily stage entries",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        ulong cost=(ulong)Math.Max(0,stage.ConsumeStamina)*r.skipCount;if(cost>p.Stamina)return fail("not enough stamina for sweep",ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
        uint growth=(uint)Math.Max(0,stage.CardRewardExp)*r.skipCount;
        var progress=StageGrowth(p,(ulong)Math.Max(0,stage.RewardExp)*r.skipCount,(uint)cost);
        var planner=new LocalPlayer {CardIds=p.CardIds.ToList()};var parts=new List<ShopGrant>();var total=new ShopGrant();
        bool adventure=r.stageId>GameCatalog.TutorialEndStage||r.dungeonFlag;
        int multiplier=adventure?Attendance.AdventureDropChanceMultiplier(r.difficulty,r.dungeonFlag,Now):1;
        for(int i=0;i<r.skipCount;i++) {
            var part=new ShopGrant();if(!PlanStageLoot(planner,stage,false,adventure,multiplier,part))return Invalid("original sweep reward unavailable");
            planner.CardIds.AddRange(part.Cards);parts.Add(part);total.Gold+=part.Gold;total.Cash+=part.Cash;total.Cards.AddRange(part.Cards);
            foreach(var x in part.Items) {long count=(long)total.Items.GetValueOrDefault(x.Key)+x.Value;if(count>int.MaxValue)return fail("sweep reward capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);total.Items[x.Key]=(int)count;}
        }
        if(growth>0&&!AddShopReward(planner,total,BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,(int)GrowupItem,(int)growth))return Invalid("invalid sweep growth reward");
        if(progress==null||!CanPayAndGrant(p,total,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("sweep reward storage limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsNormalBattleSkipOk {earnedExp=(uint)Math.Max(0,stage.RewardExp)*r.skipCount};
        foreach(var part in parts) {
            var reward=new PKBattleSkipReward {skipSequence=(uint)result.earnedList.Count+1,earnedCoinInfo=new() {gold=(uint)part.Gold,cashPoint=(uint)part.Cash}};
            ApplyShopGrant(p,part,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,reward.earnedItemList,reward.earnedCardList);result.earnedList.Add(reward);
        }
        if(growth>0) {p.Inventory[GrowupItem]=p.Inventory.GetValueOrDefault(GrowupItem)+(int)growth;result.deltaExpItemInfo=ItemInfo(GrowupItem,(int)growth);}
        bool wasFull=p.Stamina>=catalog.StaminaMax(p.Level);(p.Exp,p.Level,p.Stamina)=progress.Value;
        if(wasFull||p.Stamina>=catalog.StaminaMax(p.Level))p.StaminaStamp=Now;
        if(stage.DailyLimit>0) {var used=BattleCountsOf(p,r.difficulty,r.dungeonFlag);used[r.stageId]=used.GetValueOrDefault(r.stageId)+r.skipCount;}
        result.exp=p.Exp;result.level=p.Level;result.coinInfo=Coins(p);result.stamina=p.Stamina;result.staminaMax=catalog.StaminaMax(p.Level);result.staminaStamp=p.StaminaStamp;
        RecordObjective(p, BscTable.CHECK_TYPE.MCT_CLEAR_PVE, r.skipCount);
        return ok(result);
    }
}
