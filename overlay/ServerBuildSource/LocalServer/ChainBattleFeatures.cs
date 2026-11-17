internal sealed partial class ProtocolRouter
{
    private void RefreshChainBattle(LocalPlayer player)
    {
        var state=player.ChainBattle;
        ulong day=TowerDay(Now);
        if(state.Day==day)return;
        state.Day=day;
        state.Used=0;
    }

    private BscTable.ChainBattleData ChainTier(LocalPlayer player) =>
        Competitive.ChainBattles.Single(x=>x.Id==player.ChainBattle.Tier);

    private BscTable.ChainBattleStepData? ChainStep(LocalPlayer player) =>
        ChainTier(player).StepDataList.SingleOrDefault(x=>x.Step==player.ChainBattle.Step);

    private void PrepareChainRun(LocalPlayer player)
    {
        var state=player.ChainBattle;
        var tier=Competitive.ChainBattles.Single(x=>player.Level>=x.PcLevelMin&&player.Level<=x.PcLevelMax);
        state.Tier=(uint)tier.Id;
        int count=tier.StepDataList.Max(x=>x.Step)+1;
        state.Rewards=new byte[count*4];
        state.Factions=Enumerable.Repeat((byte)BscTable.CARD_FACTION.FACTION_ALL,count).ToArray();
        foreach(var step in tier.StepDataList)
        {
            int selected;
            if(step.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF)
            {
                var choices=Raids.BuffSets.Single(x=>x.Id==step.BuffSetId).BuffDataList;
                selected=choices[WeightedLoot(Enumerable.Range(0,choices.Count).ToArray(),i=>Math.Max(0,choices[i].Prob))].BuffId;
            }
            else
            {
                selected=WeightedLoot(Enumerable.Range(0,step.Prob.Count).ToArray(),i=>Math.Max(0,step.Prob[i]));
                int faction=WeightedLoot(Enumerable.Range(0,step.Faction.Count).ToArray(),i=>Math.Max(0,step.FactionProb[i]));
                state.Factions[step.Step]=(byte)step.Faction[faction];
            }
            if(selected is <0 or >255)throw new InvalidDataException("Chain choice exceeds native byte layout");
            state.Rewards[step.Step*4]=(byte)selected;
        }
        state.Step=1;
        state.Battles=state.Wins=state.Buff=state.BuffRolls=0;
        state.Cards.Clear();
        state.Enemy=[];
        state.EnemyCards.Clear();
        state.Continuing=true;
    }

    private PKChainBattleRecord ChainRecord(LocalPlayer player)
    {
        var state=player.ChainBattle;
        return new PKChainBattleRecord
        {
            id=state.Tier,stepId=state.Step,continueFlag=state.Continuing,
            todayResetCount=state.Used,latestRegenResetCountTIme=state.Day*86400-4*3600,
            totalBattleCount=state.Battles,totalBattleWinCount=state.Wins,
            currentBuffId=state.Buff,buffShuffleCount=state.BuffRolls,
            rewardIndexInfo=state.Rewards.ToArray(),factionInfo=state.Factions.ToArray(),
            canRecieveFirstClearReward=!state.FirstClears.Contains(state.Tier)
        };
    }

    private bool ChainCardAvailable(LocalPlayer player,ulong sequence)
    {
        if(!Owns(player,sequence)||InTraining(player,sequence)||CardState(player,sequence).Level<20)return false;
        var state=player.ChainBattle;
        if(state.Cards.TryGetValue(sequence,out var saved)&&saved.Hp<=0)return false;
        byte faction=state.Factions[state.Step];
        return faction==(byte)BscTable.CARD_FACTION.FACTION_ALL||
            (byte)catalog.RequireCard(player.CardIds[(int)sequence-1]).Faction==faction;
    }

    private GsGetChainBattleCardInfoOk ChainPreview(LocalPlayer player)
    {
        var state=player.ChainBattle;
        if(state.Enemy.Length==0)
        {
            var step=ChainStep(player)!;
            int grade=step.MatchNumber<=3?1:step.MatchNumber<=7?2:3;
            var sources=Bots.All().OrderBy(x=>x.Uid).ToArray();
            int seed=unchecked((int)(player.Uid*31+state.Day*17+(uint)step.Step));
            var offer=sources.Length>0
                ?MakeCompetitiveOffer(player,sources[(int)((state.Day+(uint)step.Step)%(ulong)sources.Length)],grade,0)
                :new LocalCompetitiveOffer{Uid=uint.MaxValue-16,Name="武神之路守关者 · 人机",Seed=seed,Level=player.Level,Grade=grade,CreatedAt=Now};
            state.Enemy=WireCodec.Serialize(CompetitiveEnemy(CompetitiveProfile(offer)));
            store.Save();
        }
        var result=new GsGetChainBattleCardInfoOk
        {
            battleEnemyInfo=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),state.Enemy)!
        };
        result.myCardInfoList.AddRange(state.Cards.Select(x=>new PKSavedBattleCardInfo
        {
            cardSeq=x.Key,hpRatio=x.Value.Hp,spRatio=x.Value.Sp,spBubbleCount=x.Value.Bubbles
        }));
        result.enemyCardInfoList.AddRange(state.EnemyCards.Select(x=>(PKSavedBattleCardInfo)WireCodec.Deserialize(typeof(PKSavedBattleCardInfo),x)!));
        return result;
    }

    private bool ChainReward(LocalPlayer player,BscTable.ChainBattleStepData step,ShopGrant grant,ref ulong arena)
    {
        var state=player.ChainBattle;
        int index=state.Rewards[step.Step*4];
        bool first=step.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_COMBAT_FINAL&&!state.FirstClears.Contains(state.Tier);
        var tier=ChainTier(player);
        var type=first?tier.FirstRewardIdType:step.RewardIdType[index];
        int id=first?tier.FirstRewardId:step.RewardId[index];
        int quantity=first?tier.FirstRewardQuantity:step.RewardQuantity[index];
        if(type==BscTable.REWARD_TYPE.REWARD_TYPE_ARENA_POINT)
        {
            arena+=(uint)quantity;
            return (ulong)player.ArenaPoint+arena<=uint.MaxValue;
        }
        return AddShopReward(player,grant,type,id,quantity);
    }

    private Reply? HandleChainBattle(string name,object? request,LocalPlayer player,SessionKeyInfo key,
        byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool handled=request is GsGetChainBattleRecordRequest or GsResetChainBattleRecordRequest or
            GsGetChainBattleCardInfoRequest or GsChainBattleStartRequest or LocalChainBattleEndRequest or
            LocalChainBattleBuffRequest or GsShuffleChainBattleBuffRequest or GsSkipChainBattleRequest or GsGetChainBattleHistoryRequest;
        if(!handled)return null;
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        if(!FeatureOpen(player,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_PVP_CHAINBATTLE)||
            !Competitive.ChainBattles.Any(x=>player.Level>=x.PcLevelMin&&player.Level<=x.PcLevelMax))
            return fail("武神之路在角色31级开放",ServerErrorCode.ERROR_INVALID_ID);
        var state=player.ChainBattle;
        if(state.Tier==0)
        {
            PrepareChainRun(player);
            state.Continuing=false;
            store.Save();
        }
        bool mutation=request is not (GsGetChainBattleRecordRequest or GsGetChainBattleHistoryRequest or GsGetChainBattleCardInfoRequest);
        return FeatureReceipt(player,state.Receipts,name,key,payload,()=>
        {
            if(request is GsGetChainBattleRecordRequest)
            {
                if(player.Competitive.ActiveMode==16){ClearCompetitive(player);store.Save();}
                return ok(new GsGetChainBattleRecordOk{chainBattleRecord=ChainRecord(player)});
            }
            if(request is GsGetChainBattleHistoryRequest)
            {
                var history=new GsGetChainBattleHistoryOk{chainBattleRecord=ChainRecord(player)};
                history.chainBattleHistory.AddRange(state.History.Select(x=>(PKChainBattleHistoryData)WireCodec.Deserialize(typeof(PKChainBattleHistoryData),x)!));
                return ok(history);
            }
            if(request is LocalChainBattleEndRequest end)return EndChainBattle(player,end,ok,fail);
            if(player.ActiveStage!=null||player.Competitive.ActiveMode!=0)return Invalid("请先结束当前战斗");
            if(request is GsResetChainBattleRecordRequest or GsSkipChainBattleRequest)
            {
                uint limit=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.DAILY_MAX_CHAIN_BATTLE_COUNT]);
                if(state.Used>=limit)return fail("今日武神之路重置次数已用完",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                if(player.CardIds.Count(x=>x!=0)<5)return fail("武神之路需要持有至少5张卡牌",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
                uint previousStep=state.Step,previousTier=state.Tier;
                if(request is GsSkipChainBattleRequest&&previousStep<3)return Invalid("尚无可扫荡的武神之路关卡");
                // Build and validate a new run separately so inventory overflow
                // cannot consume today's attempt or change its selected rewards.
                var previous=player.ChainBattle;
                var candidate=new LocalChainBattleState
                {
                    Day=previous.Day,Used=previous.Used+1,BestWins=previous.BestWins,
                    FirstClears=previous.FirstClears.ToHashSet(),Receipts=previous.Receipts,
                    History=previous.History.ToList()
                };
                player.ChainBattle=candidate;
                PrepareChainRun(player);
                var grant=new ShopGrant();ulong arena=0;
                if(request is GsSkipChainBattleRequest)
                {
                    if(candidate.Tier!=previousTier){player.ChainBattle=previous;return Invalid("等级分组改变，请从第一关重新挑战");}
                    foreach(var row in ChainTier(player).StepDataList.Where(x=>x.Step<previousStep))
                    {
                        if(row.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF)continue;
                        if(!ChainReward(player,row,grant,ref arena)){player.ChainBattle=previous;return Invalid("武神之路扫荡奖励无法发放");}
                        candidate.Wins++;candidate.Battles++;
                    }
                    candidate.Step=previousStep;
                    candidate.Continuing=ChainStep(player)!=null;
                }
                if(!CanPayAndGrant(player,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))
                {
                    player.ChainBattle=previous;
                    return fail("请先整理背包或货币",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                }
                RecordObjective(player,BscTable.CHECK_TYPE.MCT_ENTER_CHAIN_BATTLE_PVP,1);
                if(request is GsResetChainBattleRecordRequest)return ok(new GsResetChainBattleRecordOk{chainBattleRecord=ChainRecord(player)});
                var swept=new GsSkipChainBattleOk{earnedCoinInfo=GrantCoins(grant),battleEndSyncData=new()};
                swept.earnedCoinInfo.arenaPoint=(uint)arena;
                ApplyShopGrant(player,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,swept.earnedItemList,swept.earnedCardList);
                player.ArenaPoint+=(uint)arena;
                swept.chainBattleRecord=ChainRecord(player);swept.coinInfo=Coins(player);
                return ok(swept);
            }
            var step=ChainStep(player);
            if(!state.Continuing||step==null)return Invalid("请先重置武神之路，开始本次挑战");
            if(request is LocalChainBattleBuffRequest buff)
            {
                bool scheduled=step.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF;
                uint id=scheduled?state.Rewards[step.Step*4]:buff.buffId;
                var row=Raids.Buffs.SingleOrDefault(x=>x.Id==id);
                if(row==null||!scheduled&&(state.Buff!=0||row.GameMode!=BscTable.GAME_MODE.MODE_CHAINBATTLE_PVP||row.Cost<0||row.VipLevel>PlayerVipGrade(player)))
                    return Invalid("请选择有效的武神之路增益");
                uint cost=scheduled?0:(uint)row.Cost;
                if(player.Gold<cost)return fail("购买增益所需金币不足",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
                player.Gold-=cost;state.Buff=id;
                if(scheduled){state.Step++;state.BuffRolls=0;}
                return ok(new GsBuyChainBattleBuffOk{chainBattleRecord=ChainRecord(player),coinInfo=Coins(player)});
            }
            if(request is GsShuffleChainBattleBuffRequest)
            {
                if(step.StepType!=BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF||state.BuffRolls>=100)return Invalid("当前关卡无法更换增益");
                var set=Raids.BuffSets.Single(x=>x.Id==step.BuffSetId);
                var expense=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_CHAIN_BATTLE_BUFF_SHUFFLE,(int)state.BuffRolls+1,true);
                if(expense==null||expense.Value<0)return Invalid("增益更换费用未配置");
                ulong cost=(ulong)((float)(expense.Value*set.BaseCost)/100f);
                if(cost>player.Gold)return fail("更换增益所需金币不足",ServerErrorCode.ERROR_NOT_ENOUGH_GOLD);
                var choices=set.BuffDataList;
                int selected=choices[WeightedLoot(Enumerable.Range(0,choices.Count).ToArray(),i=>Math.Max(0,choices[i].Prob))].BuffId;
                if(selected is <0 or >255)return Invalid("无效的原版增益编号");
                player.Gold-=(uint)cost;state.Rewards[step.Step*4]=(byte)selected;state.BuffRolls++;
                return ok(new GsShuffleChainBattleBuffOk{chainBattleRecord=ChainRecord(player),coinInfo=Coins(player)});
            }
            if(step.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF)return Invalid("请先确认当前关卡增益");
            if(request is GsGetChainBattleCardInfoRequest)return ok(ChainPreview(player));
            if(request is GsChainBattleStartRequest start)
            {
                if(start.stepId!=state.Step||start.deckid!=20||state.Enemy.Length==0)return Invalid("关卡或对手已改变，请重新选择卡组");
                if(!CompetitiveRoster(player,DeckSlots(player,20),5,out var cards)||cards.Any(x=>!ChainCardAvailable(player,x)))
                    return fail("卡组包含濒死、修炼、低于20级或阵营不符的卡牌",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
                ClearCompetitive(player);
                var battle=player.Competitive;
                var enemy=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),state.Enemy)!;
                battle.ActiveMode=16;battle.Opponent=enemy.uid;battle.Cards=cards;battle.Enemy=state.Enemy.ToArray();
                battle.Stamp=NextLocalBattleStamp(player);battle.OpenedAt=Now;battle.Started=true;
                LocalBattleAttempts.Bind(player,0xF0000010u,battle.Stamp);
                ConsumeBsBalls(player,cards);
                return ok(new LocalChainBattleStartOk{stamp=battle.Stamp});
            }
            return Invalid("未知的武神之路操作");
        },fail,mutation);
    }

    private Reply EndChainBattle(LocalPlayer player,LocalChainBattleEndRequest end,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
        var battle=player.Competitive;
        var state=player.ChainBattle;
        var step=ChainStep(player);
        if(end.stamp==0||battle.ActiveMode!=16||battle.Stamp!=end.stamp||!LocalBattleAttempts.Matches(player,0xF0000010u,end.stamp)||step==null)
            return fail("本场武神之路战斗已结束或已退出",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        if(end.endType is not (NormalBattleEndType.NORMAL_BATTLE_SUCCESS or NormalBattleEndType.NORMAL_BATTLE_FAIL))return Invalid("无效的战斗结果");
        bool won=end.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS;
        if(battle.ReplayWinner!=(won?0u:1u))return Invalid("武神之路回放与结算结果不一致");
        var mine=end.myCardInfoList;
        if(!mine.Select(x=>x.cardSeq).Order().SequenceEqual(battle.Cards.Order()))return Invalid("出战卡牌与结算卡牌不一致");
        var preview=(PKCloneBattleEnemyInfo)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),battle.Enemy)!;
        var enemies=end.enemyCardInfoList;
        if(!enemies.Select(x=>x.cardSeq).Order().SequenceEqual(preview.cardInfoList.Select(x=>(ulong)x.cardId).Order()))return Invalid("敌方卡牌与结算卡牌不一致");
        bool Stats(PKSavedBattleCardInfo row)=>float.IsFinite(row.hpRatio)&&row.hpRatio>=0&&row.hpRatio<=1&&
            float.IsFinite(row.spRatio)&&row.spRatio>=0&&row.spRatio<=100000&&row.spBubbleCount<=100;
        if(mine.Concat(enemies).Any(x=>!Stats(x))||won&&enemies.Any(x=>x.hpRatio>0)||!won&&mine.Any(x=>x.hpRatio>0))
            return Invalid("无效的生命、内力或胜负数据");
        var grant=new ShopGrant();ulong arena=0;
        if(won&&!ChainReward(player,step,grant,ref arena))return Invalid("武神之路关卡奖励未配置");
        if(!CanPayAndGrant(player,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("请先整理背包或货币",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsChainBattleEndConfirmOk{earnedCoinInfo=GrantCoins(grant),battleEndSyncData=BallSync(player)};
        ApplyShopGrant(player,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);
        player.ArenaPoint+=(uint)arena;result.earnedCoinInfo.arenaPoint=(uint)arena;
        foreach(var card in mine)state.Cards[card.cardSeq]=new(){Hp=card.hpRatio,Sp=card.spRatio,Bubbles=card.spBubbleCount};
        state.EnemyCards=enemies.Select(WireCodec.Serialize).ToList();
        state.Battles++;
        if(won)
        {
            state.Wins++;state.BestWins=Math.Max(state.BestWins,state.Wins);
            if(step.StepType==BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_COMBAT_FINAL)state.FirstClears.Add(state.Tier);
            state.Step++;state.Buff=0;state.BuffRolls=0;state.Enemy=[];state.EnemyCards.Clear();
        }
        state.Continuing=ChainStep(player)!=null;
        if(state.Continuing&&ChainStep(player)!.StepType!=BscTable.CHAIN_BATTLE_STEP_TYPE.CHAIN_BATTLE_STEP_BUFF)
            state.Continuing=Enumerable.Range(1,player.CardIds.Count).Any(x=>ChainCardAvailable(player,(ulong)x));
        var first=preview.cardInfoList[0];
        var history=new PKChainBattleHistoryData
        {
            battleTime=Now,battleResult=won?0u:1u,enemyUid=preview.uid,enemyCardId=first.cardId,
            replaySeq=battle.Stamp,enemyCardLevel=first.cardLevel,enemyCardEnchantGrade=first.cardEnchant,
            enemycommonInfo=preview.commonInfo,enemyCardSkinId=first.cardSkinId
        };
        state.History.Insert(0,WireCodec.Serialize(history));
        if(state.History.Count>20)state.History.RemoveAt(state.History.Count-1);
        ClearCompetitive(player);
        result.chainBattleRecord=ChainRecord(player);result.coinInfo=Coins(player);
        return ok(result);
    }
}
