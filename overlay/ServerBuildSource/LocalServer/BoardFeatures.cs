using System.Security.Cryptography;
using System.Text.Json;
internal sealed class LocalBoardState
{
    [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public uint DefaultPoints {get;set;}
    public uint Map {get;set;}
    public uint Node {get;set;}
    public uint Next {get;set;}
    public uint Move {get;set;}
    public BATTLE_BOARD_STATUS Status {get;set;}
    public uint Resets {get;set;}
    public uint Bought {get;set;}
    public uint Skips {get;set;}
    public byte[] Rewards {get;set;}=[];
    public HashSet<uint> Completed {get;set;}=[];
    public byte[] EnemyData {get;set;}=[];
    [System.Text.Json.Serialization.JsonIgnore] public PKCloneBattleEnemyInfo? Enemy {get=>EnemyData.Length==0?null:(PKCloneBattleEnemyInfo?)WireCodec.Deserialize(typeof(PKCloneBattleEnemyInfo),EnemyData);set=>EnemyData=value==null?[]:WireCodec.Serialize(value);}
    public ulong ActiveStamp {get;set;}
}
internal sealed partial class ProtocolRouter
{
    private BoardResourceCatalog? boardResourceCatalog;
    private BoardResourceCatalog BoardResources=>boardResourceCatalog??=new(RootDirectory);
    internal static void RefreshBoardDay(LocalBoardState b,int defaultPoints,ulong now)
    {
        ulong day=(now+8*3600)/86400;
        if(b.Day==day)return;b.Day=day;b.DefaultPoints=(uint)Math.Max(0,defaultPoints);b.Resets=0;b.Bought=0;
    }
    private void RefreshBoardResources(LocalPlayer p)
    {
        ulong before=p.Board.Day,resourceBefore=p.Resources.Day;
        RefreshBoardDay(p.Board,catalog.VipFor(p.VipExp).BattleBoardDicePoint,Now);RefreshResourceDay(p.Resources,Now);
        if(before!=p.Board.Day||resourceBefore!=p.Resources.Day)store.Save();
    }
    private void CreateBoard(LocalPlayer p)
    {
        var b=p.Board;var maps=BoardResources.Boards.Where(x=>x.RequirePcLevelMin<=p.Level&&x.RequirePcLevelMax>=p.Level&&x.Prob>0).ToArray();
        if(maps.Length==0)throw new InvalidDataException("No original board for this level");
        var map=WeightedLoot(maps,x=>x.Prob);b.Map=(uint)map.Id;b.Node=(uint)map.SpotList[0].SpotId;b.Next=(uint)map.SpotList[0].FirstNextSpotId;b.Move=0;b.Status=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_DICE_WAIT;b.Enemy=null;b.ActiveStamp=0;b.Skips=0;
        b.Rewards=new byte[map.SpotList.Max(x=>x.SpotId)+1];
        foreach(var spot in map.SpotList)if(spot.RewardId.Count>0)b.Rewards[spot.SpotId]=(byte)WeightedLoot(Enumerable.Range(0,spot.RewardId.Count).ToArray(),i=>spot.Prob.Count==spot.RewardId.Count?spot.Prob[i]:1);
    }
    private BscTable.BattleBoardData BoardMap(LocalPlayer p)=>BoardResources.Boards.Single(x=>x.Id==p.Board.Map);
    private PKBattleBoardPlayInfo BoardInfo(LocalPlayer p)
    {
        var b=p.Board;var map=BoardMap(p);
        return new(){mapId=b.Map,currentNode=b.Node,nextNode=b.Next,remainMovePoint=b.Move,status=b.Status,defaultDicePoint=b.DefaultPoints,additionalDicePoint=p.Missions.DicePoints,lastFactionLimit=(uint)BscTable.CARD_FACTION.FACTION_ALL,todayResetCount=b.Resets,dicePointBonusBuyCount=b.Bought,battleSkipCount=b.Skips,rewardIndexInfo=b.Rewards,canRecieveFirstReward=!b.Completed.Contains(b.Map)&&map.FirstRewardIdType!=BscTable.REWARD_TYPE.REWARD_NONE};
    }
    private Reply? HandleBoard(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool buy=request is GsBuyBonusCountRequest bonus&&bonus.priceType==BscTable.VIP_PRICE_DISCOUNT_TYPE.VIP_PRICE_DISCOUNT_BUY_BATTLE_BOARD_DICE_POINT;
        if(!buy&&name is not ("GS_GET_BATTLE_BOARD_PLAY_INFO_REQUEST" or "GS_RESET_BATTLE_BOARD_PLAY_INFO_REQUEST" or "GS_CAST_DICE_BATTLE_BOARD_REQUEST" or "GS_MOVE_PAWN_BATTLE_BOARD_REQUEST" or "GS_SELECT_BATTLE_BOARD_WAY_REQUEST" or "GS_GET_BATTLE_BOARD_ENEMY_INFO_REQUEST" or "GS_BATTLE_BOARD_START_REQUEST" or "GS_BATTLE_BOARD_END_CONFIRM_REQUEST" or "GS_BUY_BATTLE_BOARD_DICE_COUNT_REQUEST"))return null;
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_BATTLE_BOARD))return fail("original board unlock level not reached",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        return FeatureReceipt(p,p.BoardReceipts,name,session,payload,()=>ExecuteBoard(name,request,p,ok,fail),fail);
    }
    private Reply ExecuteBoard(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string text)=>fail(text,ServerErrorCode.ERROR_INVALID_ID);var b=p.Board;
        if(b.Map==0){if(name!="GS_GET_BATTLE_BOARD_PLAY_INFO_REQUEST")return Invalid("open the board first");CreateBoard(p);}
        if(name=="GS_GET_BATTLE_BOARD_PLAY_INFO_REQUEST") {if(b.ActiveStamp!=0){RememberBattleStamps(p);ClearBoardAttempt(p);store.Save();}return ok(new GsGetBattleBoardPlayInfoOk{playInfo=BoardInfo(p)});}
        var map=BoardMap(p);var node=map.SpotList.Single(x=>x.SpotId==b.Node);
        if(p.ActiveStage!=null&&name!="GS_BATTLE_BOARD_END_CONFIRM_REQUEST")return Invalid("finish the active battle first");
        if(name=="GS_RESET_BATTLE_BOARD_PLAY_INFO_REQUEST"){
            if(b.Resets>=catalog.VipFor(p.VipExp).BattleBoardResetCount)return fail("daily board reset limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            CreateBoard(p);b.Resets++;return ok(new GsResetBattleBoardPlayInfoOk{playInfo=BoardInfo(p)});
        }
        if(name is "GS_BUY_BONUS_COUNT_REQUEST" or "GS_BUY_BATTLE_BOARD_DICE_COUNT_REQUEST") {
            uint points=request is GsBuyBonusCountRequest r?r.buyCount:100;uint unit=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BATTLE_BOARD_DICE_POINT_PER_CAST];
            if(points==0||points%unit!=0||(ulong)b.Bought+points>(ulong)Math.Max(0,catalog.VipFor(p.VipExp).BattleBoardBonusDicePoint))return Invalid("invalid or exhausted VIP dice-point purchase");
            var prices=Raids.Prices.Single(x=>x.VipRiceDiscountType==BscTable.VIP_PRICE_DISCOUNT_TYPE.VIP_PRICE_DISCOUNT_BUY_BATTLE_BOARD_DICE_POINT).PriceDiscountBuyCountInfoList;
            var fee=prices.FirstOrDefault(x=>x.Count==points)??prices.FirstOrDefault(x=>x.Count==0);
            if(fee==null||fee.Price<0)return Invalid("original dice price missing");ulong cost=fee.Count==0?(ulong)fee.Price*points:(ulong)fee.Price;
            if(cost>p.CashPoint||(ulong)p.Missions.DicePoints+points>uint.MaxValue)return fail("not enough diamonds or dice-point capacity",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
            p.CashPoint-=(uint)cost;p.Missions.DicePoints+=points;b.Bought+=points;
            return name=="GS_BUY_BONUS_COUNT_REQUEST"?ok(new GsBuyBonusCountOk{coinInfo=Coins(p),todayBuyCount=b.Bought,changedValue=p.Missions.DicePoints,priceType=BscTable.VIP_PRICE_DISCOUNT_TYPE.VIP_PRICE_DISCOUNT_BUY_BATTLE_BOARD_DICE_POINT}):ok(new GsBuyBattleBoardDiceCountOk{playInfo=BoardInfo(p),nowCoinInfo=Coins(p)});
        }
        if(name=="GS_CAST_DICE_BATTLE_BOARD_REQUEST") {
            if(b.Status is not (BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_DICE_WAIT or BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_MOVE_WAIT))return Invalid("board is not waiting for a die");
            uint cost=(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BATTLE_BOARD_DICE_POINT_PER_CAST];if((ulong)b.DefaultPoints+p.Missions.DicePoints<cost)return fail("not enough dice points",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var die=BoardResources.Dice.Single(x=>x.DiceId==map.DiceId);uint value=(uint)WeightedLoot(Enumerable.Range(0,die.DiceNum.Count).ToArray(),i=>die.DiceProb[i]);value=(uint)die.DiceNum[(int)value];
            uint use=Math.Min(cost,b.DefaultPoints);b.DefaultPoints-=use;p.Missions.DicePoints-=cost-use;b.Move=value;if(b.Next==0)b.Next=(uint)node.FirstNextSpotId;b.Status=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_MOVE_WAIT;

            return ok(new GsCastDiceBattleBoardOk{diceValue=value,playInfo=BoardInfo(p),nowCoinInfo=Coins(p),rewardCoinInfo=new(),exp=p.Exp,level=p.Level,stamina=p.Stamina,staminaMax=catalog.StaminaMax(p.Level),staminaStamp=p.StaminaStamp});
        }
        if(request is GsSelectBattleBoardWayRequest way) {
            if(b.Status!=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_WAY_SELECT||way.spotId==0||way.spotId!=node.FirstNextSpotId&&way.spotId!=node.SecondNextSpotId)return Invalid("invalid branch selection");
            b.Next=way.spotId;b.Status=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_DICE_WAIT;return ok(new GsSelectBattleBoardWayOk{playInfo=BoardInfo(p)});
        }
        if(request is GsMovePawnBattleBoardRequest move) {
            if(b.Status!=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_MOVE_WAIT||move.spotId!=b.Next||b.Move is 0 or >6)return Invalid("invalid board move");
            var dest=map.SpotList.Single(x=>x.SpotId==b.Next);for(int i=1;i<b.Move&&dest.FirstNextSpotId!=0;i++)dest=map.SpotList.Single(x=>x.SpotId==dest.FirstNextSpotId);
            var grant=new ShopGrant();bool fight=(int)dest.BattleSpotType is >=4 and <=6;bool end=dest.BattleSpotType==BscTable.BATTLE_SPOT_TYPE.BATTLE_SPOT_END;
            if(!fight&&!BoardReward(p,dest,end,grant))return Invalid("original board reward unavailable");
            if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("board reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsMovePawnBattleBoardOk{rewardCoinInfo=GrantCoins(grant),exp=p.Exp,level=p.Level,stamina=p.Stamina,staminaMax=catalog.StaminaMax(p.Level),staminaStamp=p.StaminaStamp};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.rewardItemList,result.rewardCardList);
            b.Node=(uint)dest.SpotId;b.Next=(uint)dest.FirstNextSpotId;b.Move=0;b.Enemy=null;b.Status=end?BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_COMPLETE:fight?BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_BATTLE:dest.BattleSpotType==BscTable.BATTLE_SPOT_TYPE.BATTLE_SPOT_BRANCH?BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_WAY_SELECT:BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_DICE_WAIT;
            if(end){b.Completed.Add(b.Map);}
            result.playInfo=BoardInfo(p);result.nowCoinInfo=Coins(p);return ok(result);
        }
        if(name=="GS_GET_BATTLE_BOARD_ENEMY_INFO_REQUEST") {
            if(b.Status!=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_BATTLE)return Invalid("not on a battle spot");
            b.Enemy??=BoardEnemy(p);return ok(new GsGetBattleBoardCardInfoOk{battleEnemyInfo=b.Enemy,playInfo=BoardInfo(p)});
        }
        if(name=="GS_BATTLE_BOARD_START_REQUEST") {
            if(b.Status!=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_BATTLE||b.Enemy==null||!ValidLocalRoster(p,9))return Invalid("invalid board battle or deck");
            b.ActiveStamp=NextLocalBattleStamp(p);LocalBattleAttempts.Bind(p,b.Node,b.ActiveStamp);return ok(null);
        }
        if(request is GsBattleBoardEndConfirmRequest endBattle) {
            if(b.Status!=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_BATTLE||!LocalBattleAttempts.Matches(p,b.Node,b.ActiveStamp)||!Enum.IsDefined(endBattle.endType))return Invalid("no matching board battle");
            bool won=endBattle.endType==NormalBattleEndType.NORMAL_BATTLE_SUCCESS;var grant=new ShopGrant();uint bonus=won?(uint)catalog.Numbers[BscTable.NUMBER_TYPE.BATTLE_BOARD_WIN_DICE_BONUS]:0;
            if(won&&!BoardReward(p,node,false,grant))return Invalid("original battle reward unavailable");if((ulong)p.Missions.DicePoints+bonus>uint.MaxValue||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("board reward storage full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsBattleBoardEndConfirmOk{rewardCoinInfo=GrantCoins(grant),battleEndSyncData=new(),exp=p.Exp,level=p.Level,stamina=p.Stamina,staminaMax=catalog.StaminaMax(p.Level),staminaStamp=p.StaminaStamp};ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.rewardItemList,result.rewardCardList);
            p.Missions.DicePoints+=bonus;if(won){b.Status=BATTLE_BOARD_STATUS.BATTLE_BOARD_STATUS_DICE_WAIT;RecordObjective(p,BscTable.CHECK_TYPE.MCT_WIN_BATTLE_BOARD_BATTLE);}ClearBoardAttempt(p);result.playInfo=BoardInfo(p);result.nowCoinInfo=Coins(p);return ok(result);
        }
        return Invalid("unsupported board state");
    }
    private bool ValidLocalRoster(LocalPlayer p,uint deck)=>DeckSlots(p,deck).Any(x=>x!=0)&&DeckSlots(p,deck).Where(x=>x!=0).Distinct().Count()==DeckSlots(p,deck).Count(x=>x!=0)&&DeckSlots(p,deck).Where(x=>x!=0).All(x=>Owns(p,x)&&!InTraining(p,x));
    private bool BoardReward(LocalPlayer p,BscTable.BattleSpotInfo spot,bool finish,ShopGrant grant)
    {
        var map=BoardMap(p);if(finish&&!p.Board.Completed.Contains(p.Board.Map)&&map.FirstRewardIdType!=BscTable.REWARD_TYPE.REWARD_NONE)return AddShopReward(p,grant,map.FirstRewardIdType,map.FirstRewardId,map.FirstRewardQuantity);
        int i=p.Board.Rewards[spot.SpotId];return spot.RewardId.Count==0||AddShopReward(p,grant,spot.RewardIdType[i],spot.RewardId[i],spot.RewardQuantity[i]);
    }
    private PKCloneBattleEnemyInfo BoardEnemy(LocalPlayer p)
    {
        var row=BoardMap(p).SpotList.Single(x=>x.SpotId==p.Board.Node);int level=(int)Math.Max(1,p.Level-(row.BattleSpotType==BscTable.BATTLE_SPOT_TYPE.BATTLE_SPOT_BATTLE_HARD?0:row.BattleSpotType==BscTable.BATTLE_SPOT_TYPE.BATTLE_SPOT_BATTLE_NORMAL?5:10));
        var cards=catalog.UsableHeroes.Where(x=>x.Rank>=3&&x.Rank<=5).GroupBy(x=>x.EvolveFirstAncestor).Select(x=>x.First()).OrderBy(x=>x.Id).Skip((int)(p.Board.Node%10)).Take(5).ToList();
        var enemy=new PKCloneBattleEnemyInfo{uid=0,commonInfo=new(){nick="妫嬬洏瀹堝崼",level=(uint)level},leaderCardId=(uint)cards[0].Id,leaderCardLevel=(uint)level,formationInfo=new(){id=1,level=1}};
        foreach(var card in cards){var info=new PKCloneBattleCardInfo{cardId=(uint)card.Id,cardLevel=(uint)level};info.skillLevel.AddRange(Enumerable.Repeat(1u,7));enemy.cardInfoList.Add(info);}return enemy;
    }
}
