internal sealed partial class ProtocolRouter
{
    private readonly Dictionary<string,(string Fingerprint,Reply Reply)> progressionReceipts=[];
    private Reply? HandleProgressionWithReceipt(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,
        Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        bool receipt=name is "GS_EXCHANGE_STAMINA_REQUEST" or "GS_SELL_USER_CARD_REQUEST" or "GS_USE_RECOVERY_ITEM_REQUEST";
        string key=$"{session.loginSessionKey}/{name}/{session.sequenceNo}";
        string fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        bool growth=name is "GS_LEVELUP_CARD_REQUEST" or "GS_CARD_SKILL_LEVELUP_REQUEST" or "GS_ENCHANT_CARD_REQUEST" or "GS_EVOLUTION_CARD_REQUEST" or "GS_USER_SKILL_POINT_CHARGE_REQUEST" or "GS_SKILL_ENCHANT_REQUEST";
        string growthKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        if(growth && p.GrowthReceipts.TryGetValue(growthKey,out var saved))
            return fingerprint==saved.Fingerprint ? new Reply(saved.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(saved.TypeName,true),Convert.FromBase64String(saved.Payload)),saved.Name)
                : fail("request sequence reused with different data",ServerErrorCode.ERROR_INVALID_ID);
        if(receipt && progressionReceipts.TryGetValue(key,out var prior))
            return fingerprint==prior.Fingerprint ? prior.Reply : fail("request sequence reused with different data",ServerErrorCode.ERROR_INVALID_ID);
        var result=HandleDebugGrowth(name,request,p,ok,fail) ?? HandleGrowth(name,request,p,ok,fail) ?? HandleProgression(name,request,p,ok,fail);
        if(growth && result!=null && result.Name.EndsWith("_OK")) {
            if(p.GrowthReceipts.Count>=64)p.GrowthReceipts.Remove(p.GrowthReceipts.Keys.First());
            p.GrowthReceipts[growthKey]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        if(receipt && result!=null && result.Name.EndsWith("_OK")) {
            if(progressionReceipts.Count>=256) progressionReceipts.Remove(progressionReceipts.Keys.First());
            progressionReceipts[key]=(fingerprint,result);
        }
        return result;
    }
    private const uint GrowupItem = 210002001;
    private bool Owns(LocalPlayer p, ulong seq) => seq > 0 && seq <= (ulong)p.CardIds.Count && p.CardIds[(int)seq - 1] != 0;
    private LocalCardState CardState(LocalPlayer p, ulong seq)
    {
        if (!p.CardStates.TryGetValue(seq,out var state)) p.CardStates[seq] = state = new();
        return state;
    }
    private List<ulong> DeckSlots(LocalPlayer p,uint id) => id == 1 ? p.Deck : p.Decks.GetValueOrDefault(id,p.Deck);
    private List<PKHelperInfo> HelperList(LocalPlayer p) => catalog.Helpers.Select(h => new PKHelperInfo {
        helperId=(uint)h.HelperType,costumeId=p.HelperCostumes.GetValueOrDefault((uint)h.HelperType,(uint)h.DefaultId) }).ToList();
    // Original Inventory indexes stackables by ID only when itemSeq is zero.
    // A fabricated serial makes crafting/socket UI see an empty phantom stack.
    private PKUserItem ItemInfo(uint id,int count) => new() {itemID=id,itemCount=count,itemSeq=0,registTime=Now};
    private List<ulong> Collection(LocalPlayer p)
    {
        p.CollectedCardIds.UnionWith(p.CardIds.Where(id=>id != 0));
        var result = new ulong[(catalog.Cards.Max(c=>c.CollectionId)+63)/64];
        foreach (var card in catalog.Cards.Where(c=>p.CollectedCardIds.Contains((uint)c.Id) && c.CollectionId > 0))
            result[(card.CollectionId-1)/64] |= 1UL << ((card.CollectionId-1)%64);
        return result.ToList();
    }
    private void RefreshTime(LocalPlayer p)
    {
        RefreshSkillPoints(p);
        var before=(p.Stamina,p.StaminaStamp,p.ExchangeDay,p.StaminaBuyCount,p.StaminaLiteBuyCount);
        bool initialized=false;
        foreach(var id in Enum.GetValues<BscTable.USER_DECK_INDEX_TYPE>().Where(x=>(uint)x>1 && !x.ToString().EndsWith("MAX")))
            if(!p.Decks.ContainsKey((uint)id)) {p.Decks[(uint)id]=p.Deck.ToList();initialized=true;}
        if(!Owns(p,p.LeaderSequence)) {p.LeaderSequence=(ulong)p.CardIds.FindIndex(id=>id!=0)+1;initialized=true;}
        ulong now=Now,period=(ulong)Math.Max(1,catalog.Numbers[BscTable.NUMBER_TYPE.STAMINA_RECOVERY_TIME]);
        if (p.StaminaStamp == 0 || p.StaminaStamp > now) p.StaminaStamp=now;
        uint max=catalog.StaminaMax(p.Level);
        if (p.Stamina >= max) p.StaminaStamp=now;
        else {
            ulong ticks=(now-p.StaminaStamp)/period;
            if (ticks>0) { p.Stamina=(uint)Math.Min(max,p.Stamina+ticks); p.StaminaStamp=p.Stamina==max ? now : p.StaminaStamp+ticks*period; }
        }
        ulong day=(now+8*3600)/86400;
        if (p.ExchangeDay != day) {
            p.ExchangeDay=day;p.StaminaBuyCount=0;p.StaminaLiteBuyCount=0;p.SkillPointChargeCount=0;
            p.GoldExchangeCount=0;p.GoldExchangeMisses=0;p.GoldCriticalCount=0;p.GoldCriticalStreak=0;p.GoldCriticalForbid=0;
            p.SystemShopPurchases.Clear();
            // Hard/Legend daily entry limits come from BattleStageLink.DailyLimit.
            p.HardBattleCounts.Clear();p.LegendBattleCounts.Clear();
            p.HardBattleResetCounts.Clear();p.LegendBattleResetCounts.Clear();
            p.HardDungeonBattleCounts.Clear();p.HardDungeonResetCounts.Clear();
            initialized=true;
        }
        if(initialized || before.Stamina!=p.Stamina || (p.Stamina<max && before.StaminaStamp!=p.StaminaStamp) || before.ExchangeDay!=p.ExchangeDay || before.StaminaBuyCount!=p.StaminaBuyCount || before.StaminaLiteBuyCount!=p.StaminaLiteBuyCount) store.Save();
    }
    private Reply? HandleProgression(string name, object? request, LocalPlayer p, Func<object?,Reply> ok,
        Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason) => fail(reason,ServerErrorCode.ERROR_INVALID_CARD);
        switch(name)
        {
            case "GS_GET_TUTORIAL_STATUS_REQUEST":
                return ok(new GsGetTutorialStatusOk {tutorialStatus=p.TutorialStatus,tutorialFlags=Enumerable.Repeat((byte)255,20).ToArray()});
            case "GS_UPDATE_USER_INTRO_MESSAGE_REQUEST":
                string intro=((GsUpdateUserIntroMessageRequest)request!).introMsg ?? "";
                if (intro.Length>120) return fail("introduction too long",ServerErrorCode.ERROR_INVALID_ID);
                p.Intro=intro;store.Save();return ok(null);
            case "GS_UPDATE_USER_FEELING_REQUEST":
                var feeling=((GsUpdateUserFeelingRequest)request!).feelingType;
                if (!Enum.IsDefined(feeling)) return fail("invalid feeling",ServerErrorCode.ERROR_INVALID_ID);
                p.Feeling=feeling;store.Save();return ok(null);
            case "GS_UPDATE_USER_CARD_PROTECT_REQUEST":
                var protects=((GsUpdateProtectCardRequest)request!).protectList;
                if (protects.Count==0 || protects.Any(x=>!Owns(p,x.cardSeq) || x.protectValue>1)) return Invalid("invalid protection list");
                foreach(var change in protects) CardState(p,change.cardSeq).Protect=change.protectValue;
                store.Save();return ok(null);
            case "GS_UPDATE_CARD_COLOR_REQUEST":
                var colors=((GsUpdateCardColorRequest)request!).updateCardColorList;
                // APK packs hair/body hue into the low two bytes, not an enum.
                if(colors.Count==0 || colors.Any(x=>!Owns(p,x.cardSeq) || x.colorValue>ushort.MaxValue)) return Invalid("invalid card color");
                foreach(var change in colors) CardState(p,change.cardSeq).Color=change.colorValue;
                store.Save();return ok(null);
            case "GS_SET_LEADER_CARD_REQUEST":
                ulong leader=((GsSetLeaderCardRequest)request!).leaderCardSeq;
                if(!Owns(p,leader)) return Invalid("leader must be an owned card");
                p.LeaderSequence=leader;store.Save();return ok(null);
            case "GS_GET_LEADER_CARD_DETAIL_INFO_REQUEST":
                uint target=((GsGetLeaderCardDetailInfoRequest)request!).leaderCardUid;
                if (target!=0 && target!=p.Uid) return fail("local player not found",ServerErrorCode.ERROR_INVALID_ID);
                return ok(new GsGetLeaderCardDetailInfoOk {cardInfo=CardInfo(p.CardIds[(int)p.LeaderSequence-1],p.LeaderSequence,p)});
            case "GS_GET_USING_CARD_LIST_REQUEST":
                var usingCards=new GsGetUsingCardListOk {leaderCardSeq=p.LeaderSequence};
                usingCards.arenaDefenceCardSeqList.AddRange(DeckSlots(p,8).Where(x=>x!=0));
                usingCards.exhibitionCardSeqList.AddRange(DeckSlots(p,10).Where(x=>x!=0));
                usingCards.castleDefenceCardSeqList.AddRange(DeckSlots(p,55).Where(x=>x!=0));
                return ok(usingCards);
            case "GS_SELL_USER_CARD_REQUEST":
                var sale=((GsSellUserCardRequest)request!).cardSeqList;
                if(sale.Count==0 || sale.Distinct().Count()!=sale.Count || sale.Any(x=>!Owns(p,x))) return Invalid("invalid sale list");
                var used=p.Decks.Values.SelectMany(d=>d).Concat(p.Deck).Append(p.LeaderSequence).ToHashSet();
                if(p.ActiveStage!=null || sale.Any(x=>InTraining(p,x) || used.Contains(x) || CardState(p,x).Protect!=0 || HasLoadout(CardState(p,x)))) return Invalid("protected, equipped, training or active battle card cannot be sold");
                ulong gold=(ulong)p.Gold+(ulong)sale.Sum(seq=>(long)Math.Max(0,catalog.RequireCard(p.CardIds[(int)seq-1]).SellPrice));
                if(gold>uint.MaxValue) return fail("gold limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                Collection(p); // Historical collection survives selling the last copy.
                foreach(ulong seq in sale) {p.CardIds[(int)seq-1]=0;p.CardStates.Remove(seq);}
                p.Gold=(uint)gold;store.Save();return ok(new GsSellUserCardOk {currentCoinInfo=Coins(p)});
            case "GS_GET_CARD_COLLECTION_INFO_REQUEST":
                uint collectionTarget=((GsGetCardCollectionInfoRequest)request!).targetUid;
                if(collectionTarget!=0 && collectionTarget!=p.Uid) return fail("local collection not found",ServerErrorCode.ERROR_INVALID_ID);
                var book=new GsGetCardCollectionInfoOk {targetUid=p.Uid};book.currentCardCollectInfo.AddRange(Collection(p));return ok(book);
            case "GS_BUY_CARD_SKIN_REQUEST":
                uint skinId=((GsBuyCardSkinRequest)request!).cardSkinId;
                var skin=catalog.Skins.FirstOrDefault(s=>s.Id==skinId && s.Sell);
                if(skin==null || skin.SkinItemId<=0) return fail("skin is not sold in this original version",ServerErrorCode.ERROR_INVALID_CARD_SKIN);
                uint skinItem=(uint)skin.SkinItemId;
                if(p.Inventory.GetValueOrDefault(skinItem)>0) return ok(new GsBuyCardSkinOk {coinInfo=Coins(p)});
                if(catalog.VipFor(p.VipExp).VipLv<skin.OpenVipLevel) return fail("skin VIP requirement",ServerErrorCode.ERROR_INVALID_VIP_LEVEL);
                uint price=(uint)Math.Max(0,skin.SellPrice);
                if(p.CashPoint<price) return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
                p.CashPoint-=price;p.Inventory[skinItem]=1;store.Save();
                var purchase=new GsBuyCardSkinOk {coinInfo=Coins(p)};purchase.deltaItemList.Add(ItemInfo(skinItem,1));return ok(purchase);
            case "GS_CHANGE_CARD_SKIN_REQUEST":
                var wear=(GsChangeCardSkinRequest)request!;
                if(!Owns(p,wear.cardSeq)) return Invalid("card is not owned");
                if(wear.cardSkinId!=0) {
                    var appearance=catalog.Skins.FirstOrDefault(s=>s.Id==wear.cardSkinId);
                    var card=catalog.RequireCard(p.CardIds[(int)wear.cardSeq-1]);
                    if(appearance==null || p.Inventory.GetValueOrDefault((uint)appearance.SkinItemId)<=0 || appearance.AncestorCardId!=card.EvolveFirstAncestor)
                        return fail("skin is not owned or belongs to another character",ServerErrorCode.ERROR_INVALID_CARD_SKIN);
                }
                CardState(p,wear.cardSeq).Skin=wear.cardSkinId;store.Save();
                return ok(new GsChangeCardSkinOk {cardInfo=CardInfo(p.CardIds[(int)wear.cardSeq-1],wear.cardSeq,p)});
            case "GS_SELECT_HELPER_REQUEST":
                uint helper=((GsSelectHelperRequest)request!).selectedHelperType;
                if(!catalog.Helpers.Any(h=>(uint)h.HelperType==helper)) return fail("unknown helper",ServerErrorCode.ERROR_INVALID_ID);
                p.HelperId=helper;store.Save();return ok(new GsSelectHelperOk {nameCardInfo=NameCard(p)});
            case "GS_UPDATE_HELPER_COSTUME_REQUEST":
                var costume=(GsUpdateHelperCostumeRequest)request!;
                var helperData=catalog.Helpers.FirstOrDefault(h=>(uint)h.HelperType==costume.helperId);
                if(helperData==null || (costume.itemId!=helperData.DefaultId && (helperData.FindModel((int)costume.itemId)==null || p.Inventory.GetValueOrDefault(costume.itemId)<=0)))
                    return fail("helper costume is not owned",ServerErrorCode.ERROR_INVALID_ID);
                p.HelperCostumes[costume.helperId]=costume.itemId;store.Save();
                var dressed=new GsUpdateHelperCostumeOk {nameCardInfo=NameCard(p)};dressed.helperInfoList.AddRange(HelperList(p));return ok(dressed);
            case "GS_GET_EXCHANGE_INFO_REQUEST":
                var exchange=new GsGetExchangeInfoOk {exchangeToGoldCount=(int)p.GoldExchangeCount,exchangeToStaminaCount=p.StaminaBuyCount,exchangeToStaminaLiteCount=p.StaminaLiteBuyCount,exchangeGoldInfo=new PKExchangeGoldInfo {criticalAccumulateCount=p.GoldCriticalCount,criticalForbidCount=p.GoldCriticalForbid,exchangeAccumulateCount=p.GoldExchangeMisses+1}};
                exchange.exchangeId.AddRange(catalog.Exchanges.Select(x=>(uint)x.Id));return ok(exchange);
            case "GS_EXCHANGE_STAMINA_REQUEST":
                var buy=(GsExchangeStaminaRequest)request!;
                var offer=catalog.Exchanges.FirstOrDefault(x=>x.Id==buy.exchangeId);
                if(offer==null || offer.ExchangeType is not (BscTable.EXCHANGE_TYPE.EXCHANGE_TO_STAMINA or BscTable.EXCHANGE_TYPE.EXCHANGE_TO_STAMINA_LITE) || buy.tryCount>1)
                    return fail("invalid stamina offer",ServerErrorCode.ERROR_INVALID_ID);
                if(p.StaminaBuyCount+p.StaminaLiteBuyCount>=catalog.VipFor(p.VipExp).RecoveryStaminaBonusCount) return fail("daily stamina purchase limit",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                bool lite=offer.ExchangeType==BscTable.EXCHANGE_TYPE.EXCHANGE_TO_STAMINA_LITE;
                var expense=catalog.Expenses.FindItem(lite ? BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_EXCHANGE_STAMINA_LITE : BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_EXCHANGE_STAMINA,(lite ? p.StaminaLiteBuyCount : p.StaminaBuyCount)+1);
                if(expense==null || expense.Value<0) return fail("missing original cost rule",ServerErrorCode.ERROR_INVALID_ID);
                uint cost=(uint)(expense.Value*(100-Math.Clamp(offer.Discount,0,99))/100);
                if(p.CashPoint<cost) return fail("not enough diamonds",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
                if((ulong)p.Stamina+(uint)Math.Max(0,offer.ExchangeValue)>uint.MaxValue) return fail("stamina limit",ServerErrorCode.ERROR_OVER_MAX_STAMINA);
                p.CashPoint-=cost;p.Stamina+=(uint)Math.Max(0,offer.ExchangeValue);p.StaminaStamp=Now;
                if(lite)p.StaminaLiteBuyCount++;else p.StaminaBuyCount++;store.Save();
                return ok(new GsExchangeStaminaOk {currentCoinInfo=Coins(p),nowStamina=p.Stamina,staminaStamp=p.StaminaStamp,exchangeToStaminaCount=p.StaminaBuyCount,exchangeToStaminaLiteCount=p.StaminaLiteBuyCount});
            case "GS_LEVELUP_CARD_REQUEST":
                var level=(GsCardLevelUpRequest)request!;
                if(!Owns(p,level.targetCardSeq)) return Invalid("card is not owned");
                if(InTraining(p,level.targetCardSeq))return fail("finish training before leveling this card",ServerErrorCode.ERROR_CARD_IN_TRAINING);
                if(p.ActiveStage!=null)return Invalid("finish the current battle before leveling cards");
                var state=CardState(p,level.targetCardSeq);
                var data=catalog.RequireCard(p.CardIds[(int)level.targetCardSeq-1]);
                int max=ProgressionRules.CardMax(catalog,p,data);
                uint ceiling=ProgressionRules.CardCeiling(catalog,max);
                if(level.targetExp<state.Exp || level.targetExp>ceiling) return fail("card level limit",ServerErrorCode.ERROR_CARD_LEVEL_IS_OVER_LIMIT);
                uint spent=level.targetExp-state.Exp;
                int available=p.Inventory.GetValueOrDefault(GrowupItem);
                if(spent>available) return fail("not enough growth points",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
                p.Inventory[GrowupItem]=available-(int)spent;state.Exp=level.targetExp;
                state.Level=ProgressionRules.CardLevel(catalog,state.Exp,max);store.Save();
                return ok(new GsCardLevelUpOk {levelupCardInfo=CardInfo((uint)data.Id,level.targetCardSeq,p),currentCoinInfo=Coins(p),afterExpItemCount=(uint)p.Inventory[GrowupItem],deltaItemInfo=ItemInfo(GrowupItem,-(int)spent)});
            case "GS_USE_RECOVERY_ITEM_REQUEST":
                var use=(GsUseRecoveryItemRequest)request!;
                var potion=catalog.Items.FirstOrDefault(x=>x.Id==use.itemID);
                bool skillPotion=use.itemUseType==UseRecoveryItemType.RECOVERY_SKILL_POINT;
                if((!skillPotion && use.itemUseType!=UseRecoveryItemType.RECOVERY_STAMINA) || potion==null || potion.ItemSubType!=(skillPotion ? BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_SKILL_POINT : BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_RECOVERY_STAMINA) || use.itemCount==0)
                    return fail("unsupported recovery item",ServerErrorCode.ERROR_INVALID_ID);
                if(use.itemCount>p.Inventory.GetValueOrDefault(use.itemID)) return fail("not enough items",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
                if(skillPotion && p.SkillPoint>=catalog.SkillPointMax(p))return fail("skill points are already full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
                long recovered=potion.RecoveryType==BscTable.RECOVERY_TYPE.RECOVERY_TYPE_PERCENT ? (long)(skillPotion ? catalog.SkillPointMax(p) : catalog.StaminaMax(p.Level))*potion.RecoveryValue/100 : potion.RecoveryType==BscTable.RECOVERY_TYPE.RECOVERY_TYPE_VALUE ? potion.RecoveryValue : 0;
                if(recovered<=0 || recovered*use.itemCount+(skillPotion ? p.SkillPoint??0 : p.Stamina)>uint.MaxValue) return fail("invalid recovery amount",ServerErrorCode.ERROR_OVER_MAX_STAMINA);
                if(skillPotion) {p.SkillPoint=Math.Min(catalog.SkillPointMax(p),(p.SkillPoint??0)+(uint)(recovered*use.itemCount));p.SkillPointStamp=Now;}
                else {p.Stamina+=(uint)(recovered*use.itemCount);p.StaminaStamp=Now;}
                p.Inventory[use.itemID]-=(int)use.itemCount;store.Save();
                return ok(new GsUseRecoveryItemOk {itemUseType=use.itemUseType,currentCoinInfo=Coins(p),stamina=p.Stamina,staminaMax=catalog.StaminaMax(p.Level),staminaStamp=p.StaminaStamp,skillPoint=p.SkillPoint??0,skillPointStamp=p.SkillPointStamp,deltaItemInfo=ItemInfo(use.itemID,-(int)use.itemCount)});
            default:return null;
        }
    }
}
