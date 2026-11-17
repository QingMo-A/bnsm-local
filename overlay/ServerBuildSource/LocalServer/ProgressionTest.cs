using System.Net.Sockets;
using System.Text.Json;

internal static class ProgressionTest
{
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/progression",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Assert(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);
        async Task<(ushort Command,byte[] Payload,uint Sequence)> Send(TcpClient socket,ushort command,object message)
        {
            await socket.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(message),true));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response=codec.Decode((await WireCodec.ReadFrame(socket.GetStream(),timeout.Token))!,true);
            return (response.Command,response.Payload,response.Sequence);
        }
        T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="progression-test",userPW="local-only",osType=DeviceOSType.DEVICE_PC});
        var account=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true})).Payload);
        var key=new SessionKeyInfo {uid=account.uid,loginSessionKey=account.loginSessionKey};
        using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);
        var p=server.Store.Find(account.uid)!;
        async Task<byte[]> Request(GameServerCommand command,object message,bool success=true,bool retry=false)
        {
            if(!retry)key.sequenceNo++;
            var response=await Send(game,(ushort)command,message);
            string expected=command.ToString().Replace("_REQUEST",success ? "_OK":"_FAIL");
            if(((GameServerCommand)response.Command).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)response.Command);
            return response.Payload;
        }
        async Task<GsGetUserCardListOk> Cards()=>Read<GsGetUserCardListOk>(await Request(GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST,new GsGetUserCardListRquest {keyInfo=key}));
        async Task<GsGetUserGameInfoOk> Info()=>Read<GsGetUserGameInfoOk>(await Request(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest {keyInfo=key}));
        var initial=await Info();
        Assert(initial.staminaMax==150 && initial.namecardInfo.commonInfo.vipGrade==0,"Original level-one stamina capacity and VIP table");
        var protection=new GsUpdateProtectCardRequest {keyInfo=key};protection.protectList.Add(new protectCardInfo {cardSeq=2,protectValue=1});
        await Request(GameServerCommand.GS_UPDATE_USER_CARD_PROTECT_REQUEST,protection);
        Assert((await Cards()).userCardInfoList.Single(x=>x.cardSeq==2).protectCard==1,"Legacy protection request type maps to the original payload and persists");
        protection.protectList.Clear();protection.protectList.Add(new protectCardInfo {cardSeq=2});protection.protectList.Add(new protectCardInfo {cardSeq=999,protectValue=1});
        await Request(GameServerCommand.GS_UPDATE_USER_CARD_PROTECT_REQUEST,protection,false);
        Assert((await Cards()).userCardInfoList.Single(x=>x.cardSeq==2).protectCard==1,"Invalid protection batch cannot partially mutate cards");
        var color=new GsUpdateCardColorRequest {keyInfo=key};color.updateCardColorList.Add(new updateCardColorInfo {cardSeq=1,colorValue=0x8040});
        var held=new FileStream(Path.Combine(save,"players.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var release=Task.Run(async ()=>{await Task.Delay(100);held.Dispose();});
        await Request(GameServerCommand.GS_UPDATE_CARD_COLOR_REQUEST,color);
        await release;
        Assert(new PlayerStore(save,server.Catalog).Find(p.Uid)!.CardStates[1].Color==0x8040,"Transient Windows temp-save sharing lock is retried without dropping the request");
        Assert((await Cards()).userCardInfoList.Single(x=>x.cardSeq==1).cardColor==0x8040,"Original packed hair/body hue bytes are reflected in the card list");
        await Request(GameServerCommand.GS_SET_LEADER_CARD_REQUEST,new GsSetLeaderCardRequest {keyInfo=key,leaderCardSeq=2});
        var detail=Read<GsGetLeaderCardDetailInfoOk>(await Request(GameServerCommand.GS_GET_LEADER_CARD_DETAIL_INFO_REQUEST,new GsGetLeaderCardDetailInfoRequest {keyInfo=key,leaderCardUid=p.Uid}));
        Assert(detail.cardInfo.cardSeq==2 && (await Info()).namecardInfo.leaderCardInfo.cardSeq==2,"Leader selection is consistent in profile and detail APIs");
        await Request(GameServerCommand.GS_SET_LEADER_CARD_REQUEST,new GsSetLeaderCardRequest {keyInfo=key,leaderCardSeq=999},false);
        Assert(p.LeaderSequence==2,"Invalid leader cannot corrupt selection");
        var decks=new GsUpdateUserDeckListRequest {keyInfo=key};
        decks.userDeckList.Add(new PKUserDeckInfo {deckID=(BscTable.USER_DECK_INDEX_TYPE)1,cardSlot1=1,cardSlot2=2});
        decks.userDeckList.Add(new PKUserDeckInfo {deckID=(BscTable.USER_DECK_INDEX_TYPE)8,cardSlot1=3,cardSlot2=4});
        await Request(GameServerCommand.GS_UPDATE_USER_DECK_LIST_REQUEST,decks);
        var deckList=Read<GsGetUserDeckListOk>(await Request(GameServerCommand.GS_GET_USER_DECK_LIST_REQUEST,new GsGetUserDeckListRequest {keyInfo=key}));
        Assert(deckList.userDeckList.Single(x=>(uint)x.deckID==1).cardSlot1==1 && deckList.userDeckList.Single(x=>(uint)x.deckID==8).cardSlot1==3,"Adventure and arena decks are independently saved");
        decks.userDeckList[0].cardSlot1=5;decks.userDeckList[1].cardSlot1=999;
        await Request(GameServerCommand.GS_UPDATE_USER_DECK_LIST_REQUEST,decks,false);
        Assert(p.Deck[0]==1 && p.Decks[8][0]==3,"Invalid deck batch is atomic");
        var usingCards=Read<GsGetUsingCardListOk>(await Request(GameServerCommand.GS_GET_USING_CARD_LIST_REQUEST,new GsGetUsingCardListRequest {keyInfo=key}));
        Assert(usingCards.leaderCardSeq==2 && usingCards.arenaDefenceCardSeqList.SequenceEqual(new ulong[] {3,4}),"Using-card API distinguishes the saved arena deck and leader");
        var tutorial=Read<GsGetTutorialStatusOk>(await Request(GameServerCommand.GS_GET_TUTORIAL_STATUS_REQUEST,new GsGetTutorialStatusRequest {keyInfo=key}));
        Assert(tutorial.tutorialStatus==p.TutorialStatus && tutorial.tutorialFlags.Length==20,"Tutorial read returns the same stored status as startup");
        var feeling=Enum.GetValues<BscTable.USER_FEELING_TYPE>().Last();
        await Request(GameServerCommand.GS_UPDATE_USER_FEELING_REQUEST,new GsUpdateUserFeelingRequest {keyInfo=key,feelingType=feeling});
        Assert((await Info()).namecardInfo.feeling==feeling,"Profile feeling is stored and returned");
        await Request(GameServerCommand.GS_UPDATE_USER_INTRO_MESSAGE_REQUEST,new GsUpdateUserIntroMessageRequest {keyInfo=key,introMsg="本地恢复测试"});
        Assert((await Info()).namecardInfo.introMsg=="本地恢复测试","Profile introduction survives original request/read path");
        var helpers=Read<GsGetMyHelperListOk>(await Request(GameServerCommand.GS_GET_MY_HELPER_LIST_REQUEST,new GsGetMyHelperListRequest {keyInfo=key}));
        Assert(helpers.helperInfoList.Count==3 && helpers.helperInfoList.All(x=>x.costumeId!=0),"Local starter policy provides three original helpers and default costumes");
        await Request(GameServerCommand.GS_SELECT_HELPER_REQUEST,new GsSelectHelperRequest {keyInfo=key,selectedHelperType=2});
        var helper=server.Catalog.Helpers.Single(x=>(uint)x.HelperType==2);
        var dressed=Read<GsUpdateHelperCostumeOk>(await Request(GameServerCommand.GS_UPDATE_HELPER_COSTUME_REQUEST,new GsUpdateHelperCostumeRequest {keyInfo=key,helperId=2,itemId=(uint)helper.DefaultId}));
        Assert((uint)dressed.nameCardInfo.helperID==2 && dressed.nameCardInfo.helperCostumeId==helper.DefaultId,"Helper selection and default outfit round-trip");
        await Request(GameServerCommand.GS_UPDATE_HELPER_COSTUME_REQUEST,new GsUpdateHelperCostumeRequest {keyInfo=key,helperId=2,itemId=uint.MaxValue},false);
        Assert(p.HelperCostumes[2]==helper.DefaultId,"Unowned helper costume is rejected");
        // Fixture funds/items are isolated test setup, never the user's save.
        p.CashPoint=20000;p.VipExp=0;
        var skin=server.Catalog.Skins.First(s=>s.Sell && s.OpenVipLevel==0 && server.Catalog.Cards.Any(c=>c.EvolveFirstAncestor==s.AncestorCardId));
        uint cardId=(uint)server.Catalog.Cards.First(c=>c.EvolveFirstAncestor==skin.AncestorCardId).Id;
        p.CardIds.Add(cardId);ulong skinSeq=(ulong)p.CardIds.Count;
        var bought=Read<GsBuyCardSkinOk>(await Request(GameServerCommand.GS_BUY_CARD_SKIN_REQUEST,new GsBuyCardSkinRequest {keyInfo=key,cardSkinId=(uint)skin.Id}));
        Assert(bought.coinInfo.cashPoint==20000-skin.SellPrice && bought.deltaItemList.Single().itemID==skin.SkinItemId,"Skin purchase uses original price and grants its inventory token");
        await Request(GameServerCommand.GS_BUY_CARD_SKIN_REQUEST,new GsBuyCardSkinRequest {keyInfo=key,cardSkinId=(uint)skin.Id});
        Assert(p.CashPoint==20000-skin.SellPrice,"Buying an owned skin cannot charge twice");
        var worn=Read<GsChangeCardSkinOk>(await Request(GameServerCommand.GS_CHANGE_CARD_SKIN_REQUEST,new GsChangeCardSkinRequest {keyInfo=key,cardSeq=skinSeq,cardSkinId=(uint)skin.Id}));
        Assert(worn.cardInfo.cardSkinId==skin.Id,"Owned skin can be equipped on its original ancestor family");
        ulong wrongSeq=(ulong)p.CardIds.FindIndex(id=>server.Catalog.Cards.Single(c=>c.Id==id).EvolveFirstAncestor!=skin.AncestorCardId)+1;
        await Request(GameServerCommand.GS_CHANGE_CARD_SKIN_REQUEST,new GsChangeCardSkinRequest {keyInfo=key,cardSeq=wrongSeq,cardSkinId=(uint)skin.Id},false);
        Assert(p.CardStates.GetValueOrDefault(wrongSeq)?.Skin is null or 0,"Skin cannot be applied to a different character");
        await Request(GameServerCommand.GS_CHANGE_CARD_SKIN_REQUEST,new GsChangeCardSkinRequest {keyInfo=key,cardSeq=skinSeq,cardSkinId=0});
        Assert(p.CardStates[skinSeq].Skin==0,"Original appearance can be restored");
        var inventory=Read<GsGetUserItemListOk>(await Request(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest {keyInfo=key}));
        Assert(inventory.UserItemList.Any(x=>x.itemID==skin.SkinItemId && x.itemCount==1),"Skin ownership is returned by the original inventory API");
        ulong stamp=ProtocolRouter.Now-365;p.Stamina=10;p.StaminaStamp=stamp;
        var regen=await Info();
        Assert(regen.stamina==12 && regen.staminaStamp==stamp+360,"Stamina regenerates every original 180 seconds and retains partial time");
        p.Stamina=149;p.StaminaStamp=ProtocolRouter.Now-900;
        Assert((await Info()).stamina==150,"Natural stamina recovery stops at the level's original maximum");
        p.Stamina=100;p.StaminaStamp=ProtocolRouter.Now;p.ExchangeDay=(ProtocolRouter.Now+8*3600)/86400;p.StaminaBuyCount=0;
        var exchange=Read<GsGetExchangeInfoOk>(await Request(GameServerCommand.GS_GET_EXCHANGE_INFO_REQUEST,new GsGetExchangeInfoRequest {keyInfo=key}));
        Assert(exchange.exchangeId.Contains(1),"Stamina exchange exposes original offer ID");
        uint cash=p.CashPoint;
        var buy=new GsExchangeStaminaRequest {keyInfo=key,exchangeId=1};
        var replenished=Read<GsExchangeStaminaOk>(await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy));
        await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy,retry:true);
        Assert(replenished.nowStamina==350 && p.Stamina==350 && p.CashPoint==cash-50 && p.StaminaBuyCount==1,"Original 50-diamond / 250-stamina purchase and duplicate receipt");
        buy.exchangeId=uint.MaxValue;await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy,false,retry:true);
        Assert(p.StaminaBuyCount==1 && p.CashPoint==cash-50,"Same sequence with different purchase payload is rejected");
        buy.exchangeId=1;await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy);
        var limit=Read<FailPacket>(await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy,false));
        Assert(p.StaminaBuyCount==2 && limit.errorCode==(uint)ServerErrorCode.ERROR_OVER_MAX_COUNT,"Original VIP0 daily purchase limit is enforced");
        p.ExchangeDay--;await Info();Assert(p.StaminaBuyCount==0,"Stamina purchase counters reset at UTC+8 day rollover");
        p.CashPoint=0;await Request(GameServerCommand.GS_EXCHANGE_STAMINA_REQUEST,buy,false);
        Assert(p.StaminaBuyCount==0,"Insufficient funds cannot consume a daily stamina purchase");
        var potion=server.Catalog.Items.First(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_STAMINA && x.RecoveryType==BscTable.RECOVERY_TYPE.RECOVERY_TYPE_VALUE && x.RecoveryValue>0);
        p.Inventory[(uint)potion.Id]=2;p.Stamina=10;p.StaminaStamp=ProtocolRouter.Now;
        var use=new GsUseRecoveryItemRequest {keyInfo=key,itemUseType=UseRecoveryItemType.RECOVERY_STAMINA,itemID=(uint)potion.Id,itemCount=1};
        var recovery=Read<GsUseRecoveryItemOk>(await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST,use));
        await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST,use,retry:true);
        Assert(recovery.stamina==10+potion.RecoveryValue && p.Stamina==recovery.stamina && p.Inventory[(uint)potion.Id]==1,"Stamina item uses original recovery value and is replay-safe");
        use.itemCount=9;await Request(GameServerCommand.GS_USE_RECOVERY_ITEM_REQUEST,use,false);
        Assert(p.Inventory[(uint)potion.Id]==1,"Unowned item quantity cannot generate stamina");
        p.CardIds.Add(p.CardIds[0]);ulong soldSeq=(ulong)p.CardIds.Count;
        p.CardIds.Add(p.CardIds[1]);ulong survivor=(ulong)p.CardIds.Count;
        uint beforeGold=p.Gold;var sell=new GsSellUserCardRequest {keyInfo=key};sell.cardSeqList.Add(soldSeq);
        await Request(GameServerCommand.GS_SELL_USER_CARD_REQUEST,sell);uint afterGold=p.Gold;
        await Request(GameServerCommand.GS_SELL_USER_CARD_REQUEST,sell,retry:true);
        var afterCards=await Cards();
        Assert(afterGold>beforeGold && p.Gold==afterGold && !afterCards.userCardInfoList.Any(c=>c.cardSeq==soldSeq) && afterCards.userCardInfoList.Any(c=>c.cardSeq==survivor),"Card sale pays original value once and preserves all remaining sequence IDs");
        sell.cardSeqList.Clear();sell.cardSeqList.Add(2);await Request(GameServerCommand.GS_SELL_USER_CARD_REQUEST,sell,false);
        Assert(p.CardIds[1]!=0,"Leader/protected/deck cards cannot be sold");
        var book=Read<GsGetCardCollectionInfoOk>(await Request(GameServerCommand.GS_GET_CARD_COLLECTION_INFO_REQUEST,new GsGetCardCollectionInfoRequest {keyInfo=key,targetUid=p.Uid}));
        Assert(book.currentCardCollectInfo.Any(x=>x!=0),"Original collection bitset contains acquired cards");
        p.Stamina=100;p.Level=1;p.Exp=0;p.ActiveStage=null;
        uint first=server.Catalog.FirstAdventureStage;
        await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest {keyInfo=key,stageid=first+2},false);
        Assert(p.ActiveStage==null && p.Stamina==100,"Locked stage cannot be entered or consume stamina");
        var battle=Read<GsNormalBattleStartOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest {keyInfo=key,stageid=first}));
        var end=new GsNormalBattleEndConfirmRequest {keyInfo=key,stageId=first,timeStamp=battle.timeStamp};
        var reward=Read<GsNormalBattleEndConfirmOk>(await Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,end));
        Assert(reward.level==2 && reward.exp==10 && reward.staminaMax==152 && reward.stamina==battle.stamina+server.Catalog.PlayerStates.Single(x=>x.Level==2).LevelUpStaminaReward,"Adventure settlement uses original cumulative XP, stamina capacity and level-up stamina reward");
        int growth=p.Inventory.GetValueOrDefault(210002001u);
        var stageRewards=server.Catalog.Stages[first];
        int previewGrowth=stageRewards.NpcRewardId.Select((id,i)=>id==210002001?stageRewards.RewardQuantity[i]:0).Sum();
        Assert(growth==stageRewards.CardRewardExp+previewGrowth && reward.earnedItemList.Where(x=>x.itemID==210002001).Sum(x=>x.itemCount)==growth,"Advertised experience items plus declared shared card growth reach inventory exactly once");
        await Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,end,false);
        Assert(p.Inventory[210002001]==growth,"Duplicate settlement cannot grant growth points again");
        var upgrade=new GsCardLevelUpRequest {keyInfo=key,targetCardSeq=1,targetExp=10};
        var raised=Read<GsCardLevelUpOk>(await Request(GameServerCommand.GS_LEVELUP_CARD_REQUEST,upgrade));
        Assert(raised.levelupCardInfo.cardLevel==2 && raised.levelupCardInfo.cardExp==10 && raised.afterExpItemCount==growth-10,"Legacy level-up alias consumes growth points for an absolute XP target");
        await Request(GameServerCommand.GS_LEVELUP_CARD_REQUEST,upgrade);
        Assert(p.Inventory[210002001]==growth-10,"Repeated absolute level-up target cannot consume points twice");
        upgrade.targetExp=999999;await Request(GameServerCommand.GS_LEVELUP_CARD_REQUEST,upgrade,false);
        Assert(p.CardStates[1].Exp==10,"Level caps reject an oversized upgrade without mutation");
        var restored=new PlayerStore(save,server.Catalog).Find(p.Uid)!;
        Assert(restored.CardStates[1].Level==2 && restored.CardStates[2].Protect==1 && restored.LeaderSequence==2 && restored.HelperId==2 && restored.Decks[8][0]==3 && restored.Inventory[(uint)skin.SkinItemId]==1 && restored.CardIds[(int)soldSeq-1]==0,"Card state, inventory, leader, helper, independent decks and sale tombstones survive reload");
        // Exercise the original multi-OK receive loop at the maximum local capacity.
        while(p.CardIds.Count(x=>x!=0)<300)p.CardIds.Add(p.CardIds[0]);
        await game.GetStream().WriteAsync(codec.Encode((ushort)GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST,WireCodec.Serialize(new GsGetUserCardListRquest {keyInfo=key}),true));
        int count=0,parts=0,maxFrame=0;uint previousSeq=0;
        while(count<300) {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));var frame=(await WireCodec.ReadFrame(game.GetStream(),timeout.Token))!;
            var packet=codec.Decode(frame,true);var chunk=Read<GsGetUserCardListOk>(packet.Payload);
            if(parts>0 && packet.Sequence!=previousSeq+1)throw new Exception("Multipart sequence gap");
            Assert(chunk.totalDataCount==300 && chunk.dataCount==chunk.userCardInfoList.Count,"Multipart card chunk "+(++parts)+" follows original total/count contract");
            count+=(int)chunk.dataCount;previousSeq=packet.Sequence;maxFrame=Math.Max(maxFrame,frame.Length);
        }
        Assert(count==300 && parts==5 && maxFrame<=8192,"Full 300-card inventory is split into five original-size frames");
        Assert((await Info()).namecardInfo.commonInfo.level==2,"Connection remains aligned and usable after multipart response");
        // This test measures exactly 150 stackable rows; battle loot above now
        // correctly includes an equipment instance, so reset that fixture too.
        p.Inventory.Clear();p.EquipmentItems.Clear();foreach(var item in server.Catalog.Items.GroupBy(x=>x.Id).Select(x=>x.First()).Where(x=>x.Id>0).Take(150))p.Inventory[(uint)item.Id]=1;
        await game.GetStream().WriteAsync(codec.Encode((ushort)GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,WireCodec.Serialize(new GsGetUserItemListRquest {keyInfo=key}),true));
        int itemCount=0,itemParts=0;
        while(itemCount<150) {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));var frame=(await WireCodec.ReadFrame(game.GetStream(),timeout.Token))!;
            var chunk=Read<GsGetUserItemListOk>(codec.Decode(frame,true).Payload);
            if(chunk.totalDataCount!=150 || frame.Length>8192)throw new Exception("Invalid inventory chunk");itemCount+=(int)chunk.dataCount;itemParts++;
        }
        Assert(itemCount==150 && itemParts==3,"Inventory items follow the original multipart receive contract");
        var equipment=Read<GsGetUserItemListOk>(await Request(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest {keyInfo=key,reqType=(InventoryType)1}));
        Assert(equipment.totalDataCount==0,"Stackable items do not leak into equipment-only inventory requests");
        File.WriteAllText(Path.Combine(root,"evidence/progression-test.json"),JsonSerializer.Serialize(new {completedAt=DateTimeOffset.Now,passed=checks.Count,checks,saveDirectory=save,scope="Isolated encrypted TCP integration against original APK message classes. No production save changes."},new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"PASS: {checks.Count} progression integration checks.");
    }
}
