// The native castle monster is a persistent cooperative HP pool followed by an
// owner-only final battle. It is independent of castle occupation encounters.
internal sealed class LocalCastleRaid
{
    public ulong Generation {get;set;}
    public ulong SummonDay {get;set;}
    public uint Summons {get;set;}
    public FriendRaidBattleType State {get;set;}
    public uint Hp {get;set;}
    public HashSet<uint> Participants {get;set;}=[];
    public HashSet<uint> BotHelpers {get;set;}=[];
    public List<byte[]> History {get;set;}=[];
}

internal sealed partial class ProtocolRouter
{
    // Despite the native field name todayFriendRaidCount, the original UI and
    // start callback read it as remaining entries, not the number already used.
    private uint FriendRaidLeft(LocalPlayer p)=>(uint)Math.Max(0,(long)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_FRIEND_RAID_COUNT]+p.Raids.Bought.GetValueOrDefault(6)-p.Raids.Used.GetValueOrDefault(6));
    private bool CastleRaidConnected(LocalPlayer owner,BscTable.CastleSpotInfo row)=>row.LinkSpot.Count>0&&CastleSpot(owner,(uint)CastleHome(owner).MapId,(uint)row.LinkSpot[0])?.Status==1;
    private bool CastleRaidAccessible(LocalPlayer viewer,LocalPlayer owner)=>viewer.Uid==owner.Uid||viewer.Friends.Links.ContainsKey(owner.Uid)||MyGuild(viewer) is {} guild&&guild.Members.ContainsKey(owner.Uid);
    private static bool CastleRaidSummonedToday(LocalPlayer owner,ulong day)=>owner.Competitive.CastleRaidSummonDay==day||
        owner.Competitive.Castles.Values.Any(spot=>spot.Raid is {Summons:>0} raid&&raid.SummonDay==day);
    private void FillCastleRaidInfo(LocalPlayer viewer,LocalPlayer owner,LocalCastleSpot spot,PKCastleBattleMapSpotInfo info)
    {
        ulong day=TowerDay(Now);
        info.todayBossSummonCount=viewer.Uid==owner.Uid&&CastleRaidSummonedToday(owner,day)?1u:0u;
        var raid=spot.Raid;if(raid==null)return;
        // Leave history intact until the next summon. Only a cleared tile on the owner's map reopens.
        info.raidBossState=viewer.Uid==owner.Uid&&raid.State==FriendRaidBattleType.FRIEND_RAID_FINISHED&&raid.SummonDay<day
            ?FriendRaidBattleType.FRIEND_RAID_NONE:raid.State;
        info.bossHp=raid.Hp;
        info.raidBattleAvaliable=raid.State==FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE&&!raid.Participants.Contains(viewer.Uid)||raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE&&viewer.Uid==owner.Uid;
    }
    private Reply? HandleCastleRaid(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_FRIEND_RAID_HELP_SEND_FRIENDS_LIST_REQUEST")
            return CastleRaidOperation(name,request,p,ok,fail);
        if(name is not ("GS_FRIEND_RAID_BOSS_SUMMON_REQUEST" or "GS_FRIEND_RAID_BATTLE_START_REQUEST" or "GS_FRIEND_RAID_BATTLE_END_REQUEST" or "GS_GET_FRIEND_RAID_BATTLE_HISTORY_REQUEST" or "GS_BUY_FRIEND_RAID_PLAY_COUNT_REQUEST" or "GS_FRIEND_RAID_HELP_REQUEST"))return null;
        return FeatureReceipt(p,p.RaidReceipts,name,session,payload,()=>CastleRaidOperation(name,request,p,ok,fail),fail,!name.StartsWith("GS_GET_"));
    }
    private Reply BuyFriendRaidEntries(LocalPlayer p,uint count,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail,bool legacy=false)
    {
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CASTLE_MONSTER_SUMMON))return fail("castle monster level requirement",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        uint bought=p.Raids.Bought.GetValueOrDefault(6),max=(uint)Math.Max(0,catalog.VipFor(p.VipExp).FriendRaidAddBattleCount);
        if(count==0||(ulong)bought+count>max)return fail("castle monster daily purchase limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var prices=Raids.Prices.Single(x=>(int)x.VipRiceDiscountType==13).PriceDiscountBuyCountInfoList;
        var price=prices.SingleOrDefault(x=>x.Count==(int)count)??prices.SingleOrDefault(x=>x.Count==0);
        ulong cost=price==null||price.Price<0?ulong.MaxValue:price.Count==0?(ulong)price.Price*count:(uint)price.Price;
        if(cost>p.CashPoint)return fail("castle monster ticket diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
        p.CashPoint-=(uint)cost;p.Raids.Bought[6]=bought+count;
        if(legacy)return ok(new GsBuyFriendRaidCountOk{coinInfo=Coins(p),todayFriendRaidCount=FriendRaidLeft(p),buyFriendRaidCount=bought+count});
        return ok(new GsBuyBonusCountOk{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)13,todayBuyCount=bought+count,changedValue=(uint)Math.Max(0,catalog.Numbers[BscTable.NUMBER_TYPE.MAX_FRIEND_RAID_COUNT]+bought+count-p.Raids.Used.GetValueOrDefault(6)),coinInfo=Coins(p)});
    }
    private Reply CastleRaidOperation(string name,object? request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Bad(string why,ServerErrorCode code=ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO)=>fail(why,code);
        if(name=="GS_BUY_FRIEND_RAID_PLAY_COUNT_REQUEST")return BuyFriendRaidEntries(p,1,ok,fail,true);
        uint ownerId=(uint?)Field(request,"castleOwnerId")??p.Uid,mapId=(uint?)Field(request,"mapId")??0,spotId=(uint?)Field(request,"spotId")??0;
        var owner=store.Find(ownerId);
        if(owner==null||!CastleRaidAccessible(p,owner))return Bad("castle monster owner is not a friend or guild member",ServerErrorCode.ERROR_NOT_FRIEND);
        var map=Competitive.CastleMaps.FirstOrDefault(x=>x.MapId==mapId);var row=map?.SpotList.FirstOrDefault(x=>x.SpotId==spotId);
        var castle=row==null?null:Competitive.Castles.FirstOrDefault(x=>x.Id==row.CastleId&&x.BossId>0);
        var card=castle==null?null:Raids.Friend.FirstOrDefault(x=>x.Id==castle.BossId);
        var spot=CastleSpot(owner,mapId,spotId);
        if(mapId!=CastleHome(owner).MapId||row==null||castle==null||card==null||spot==null)return Bad("castle monster must be discovered on the owner's map",ServerErrorCode.ERROR_FRIEND_RAID_INFO_MISSING);
        if(request is GsGetFriendRaidBattleHistoryRequest){var history=new GsGetFriendRaidBattleHistoryOk();if(spot.Raid!=null)history.historyList.AddRange(spot.Raid.History.TakeLast(50).Reverse().Select(x=>(PKFriendRaidBattleHistory)WireCodec.Deserialize(typeof(PKFriendRaidBattleHistory),x)!));return ok(history);}
        if(request is GsFriendRaidBossSummonRequest){
            if(ownerId!=p.Uid)return Bad("castle monsters can only be summoned on your own map",ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO);
            if(p.ActiveStage!=null)return Bad("finish the active battle before summoning",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(p.Level<row.RequirePcLevel||!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CASTLE_MONSTER_SUMMON))return Bad("castle monster level requirement",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
            if(!CastleRaidConnected(owner,row))return Bad("castle monster requires its linked occupied castle");
            var prior=spot.Raid;
            if(prior?.State is FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE or FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE)return Bad("castle monster is already summoned",ServerErrorCode.ERROR_ALREADY_RAID_BOSS_EXISTS);
            ulong day=TowerDay(Now);
            if(CastleRaidSummonedToday(p,day))return Bad("castle monster daily summon limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            const uint ticket=220001003;
            if(castle.SummonCost<0||p.Inventory.GetValueOrDefault(ticket)<castle.SummonCost)return Bad("castle monster summon materials missing",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
            if(card.StatHpFriendBattle<=0||card.StatHpFinalBattle<=0)return Bad("castle monster original HP missing");
            var result=new GsFriendRaidBossSummonOk{mapId=mapId,spotId=spotId,todaySummonCount=1,raidBattleAvaliable=true};
            if(castle.SummonCost>0){p.Inventory[ticket]-=castle.SummonCost;result.deltaItemList.Add(new(){itemID=ticket,itemCount=-castle.SummonCost});}
            spot.Raid=new(){Generation=NextLocalBattleStamp(p),SummonDay=day,Summons=1,State=FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE,Hp=(uint)card.StatHpFriendBattle};
            p.Competitive.CastleRaidSummonDay=day;
            return ok(result);
        }
        var raid=spot.Raid;
        if(request is LocalCastleRaidHelpListRequest list)return CastleRaidHelpList(p,owner,row,raid,list.generation,ok,fail);
        if(request is LocalCastleRaidHelpRequest help)return CastleRaidHelp(p,owner,row,raid,card,help,ok,fail);
        if(raid==null||raid.State is FriendRaidBattleType.FRIEND_RAID_NONE or FriendRaidBattleType.FRIEND_RAID_FINISHED)return Bad("castle monster has not been summoned or was cleared",ServerErrorCode.ERROR_ALREADY_COMPLETE_FRIEND_RAID);
        if(request is GsStartFriendRaidBattleRequest start){
            if(p.ActiveStage!=null)return Bad("finish the active battle before entering castle monster",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            if(p.Level<row.RequirePcLevel||!CastleRaidConnected(owner,row))return Bad("castle monster requires its linked occupied castle");
            if(start.battleType!=raid.State||raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE&&ownerId!=p.Uid)return Bad("castle monster phase changed or final battle requires owner");
            if(raid.State==FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE&&raid.Participants.Contains(p.Uid))return Bad("castle monster participation already completed",ServerErrorCode.ERROR_ALREADY_COMPLETE_FRIEND_RAID);
            if((long)p.Raids.Used.GetValueOrDefault(6)>=catalog.Numbers[BscTable.NUMBER_TYPE.MAX_FRIEND_RAID_COUNT]+p.Raids.Bought.GetValueOrDefault(6))return Bad("castle monster daily entry limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            if(!RaidRoster(p,1))return Bad("invalid castle monster deck",ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO);
            var r=p.Raids;r.ActiveMode=raid.State==FriendRaidBattleType.FRIEND_RAID_FINAL_BATTLE?7:6;r.ActiveBoss=(uint)card.Id;r.ActiveHp=raid.Hp;r.ActiveStamp=NextLocalBattleStamp(p);
            r.CastleOwner=ownerId;r.CastleMap=mapId;r.CastleSpot=spotId;r.CastleGeneration=raid.Generation;LocalBattleAttempts.Bind(p,(uint)card.Id,r.ActiveStamp);r.Used[6]=r.Used.GetValueOrDefault(6)+1;
            RecordObjective(p,BscTable.CHECK_TYPE.MCT_ENTER_FRIENDRAID);
            return ok(new GsStartFriendRaidBattleOk{castleOwnerId=ownerId,mapId=mapId,spotId=spotId,todayFriendRaidCount=FriendRaidLeft(p),battleType=raid.State,hp=raid.Hp,sp=0,timeStamp=r.ActiveStamp});
        }
        if(request is GsEndFriendRaidBattleRequest end){
            var r=p.Raids;int mode=end.battleType==FriendRaidBattleType.FRIEND_RAID_COOPERATION_BATTLE?6:7;
            if(!RaidEndMatches(p,mode,end.bossID,end.timeStamp)||r.CastleOwner!=ownerId||r.CastleMap!=mapId||r.CastleSpot!=spotId||r.CastleGeneration!=raid.Generation||end.battleType!=raid.State)return Bad("no matching castle monster battle",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
            var cards=new[]{end.cardSlot1,end.cardSlot2,end.cardSlot3,end.cardSlot4,end.cardSlot5}.Where(x=>x!=0).ToArray();
            if(cards.Length==0||cards.Distinct().Count()!=cards.Length||cards.Any(x=>!r.BattleCards.Contains(x))||end.battleHistory==null||(int)end.battleHistory.gameMode!=(mode==6?3:4)||end.damageAmount>(mode==6?(ulong)card.DamageMax:r.ActiveHp))return Bad("castle monster result disagrees with the started roster, mode or damage",ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY);
            uint damage=(uint)Math.Min(raid.Hp,end.damageAmount),hp=raid.Hp-damage;
            bool normal=mode==6,won=normal?end.damageAmount>=(ulong)card.DamageMin:hp==0,perfect=normal&&end.damageAmount>=(ulong)card.DamageMax;
            var grant=new ShopGrant();var bonus=new ShopGrant();
            bool Plan(ShopGrant g,List<BscTable.REWARD_TYPE> types,List<int> ids,List<int> amounts){
                if(types.Count!=ids.Count||ids.Count!=amounts.Count)return false;
                for(int i=0;i<types.Count;i++){
                    if(amounts[i]<0)return false;if(amounts[i]==0)continue;
                    if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_WORLDBOSS_POINT)g.World+=(uint)amounts[i];
                    else if(types[i]==BscTable.REWARD_TYPE.REWARD_TYPE_GROUP_ITEM){if(!AddStageRow(p,g,BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM,ids[i],amounts[i]))return false;}
                    else if(!AddShopReward(p,g,types[i],ids[i],amounts[i]))return false;
                }return true;
            }
            if(won&&!(normal?Plan(grant,castle.NormalRewardIdType,castle.NormalRewardId,castle.NormalRewardQuantity):Plan(grant,castle.FinalRewardIdType,castle.FinalRewardId,castle.FinalRewardQuantity)))return Bad("castle monster original rewards missing");
            if(perfect&&!Plan(bonus,castle.PerfectRewardIdType,castle.PerfectRewardId,castle.PerfectRewardQuantity))return Bad("castle monster perfect rewards missing");
            var total=new ShopGrant{Gold=grant.Gold+bonus.Gold,Cash=grant.Cash+bonus.Cash,World=grant.World+bonus.World};
            foreach(var item in grant.Items.Concat(bonus.Items))total.Items[item.Key]=total.Items.GetValueOrDefault(item.Key)+item.Value;total.Cards.AddRange(grant.Cards);total.Cards.AddRange(bonus.Cards);
            if(!CanPayAndGrant(p,total,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return Bad("castle monster reward capacity exceeded",ServerErrorCode.ERROR_OVER_MAX_COUNT);
            var result=new GsEndFriendRaidBattleOk{castleOwnerId=ownerId,mapId=mapId,spotId=spotId,battleType=raid.State,earnedNormalCoinInfo=GrantCoins(grant),earnedPerfectCoinInfo=GrantCoins(bonus),battleEndSyncData=BallSync(p)};
            ApplyShopGrant(p,total,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.earnedItemList,result.earnedCardList);
            if(normal)ApplyCastleRaidCooperation(raid,p.Uid,damage,card);
            else{raid.Hp=hp;if(hp==0)raid.State=FriendRaidBattleType.FRIEND_RAID_FINISHED;}
            RecordCastleRaidDamage(raid,p,ownerId,mapId,spotId,card,damage,won);
            result.coinInfo=Coins(p);result.hp=raid.Hp;result.nowState=raid.State;ClearRaidBattle(p);return ok(result);
        }
        return Bad("unknown castle monster request");
    }
}
