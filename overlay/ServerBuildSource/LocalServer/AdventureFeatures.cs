using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    private int DungeonAchievementSize=>Math.Max(256,((int)catalog.Dungeons.Values.SelectMany(x=>x.Keys).DefaultIfEmpty(0u).Max()+1)*2);
    private bool CanEnterDungeon(LocalPlayer p,BscTable.GAME_DIFFICULTY difficulty,BscTable.BattleStageLink stage)=>
        StageUnlock.FieldOpen(catalog,p,difficulty,stage.FieldId) && StageUnlock.ClearStageOf(p,difficulty)>=stage.OpenStageId;
    private Reply? HandleAdventure(string name, object? request, LocalPlayer p, SessionKeyInfo session, byte[] payload,
        Func<object?,Reply> ok, Func<string,ServerErrorCode,Reply> fail)
    {
        if (request is GsNormalBattleStartRequest start) return StartAdventure(p,start,ok,fail);
        if (name is not ("GS_COMBINE_PIECE_REQUEST" or "GS_ACHIEVE_REWARD_REQUEST")) return null;
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.AdventureReceipts.TryGetValue(key,out var saved))
            return saved.Fingerprint==fingerprint
                ? new(saved.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(saved.TypeName,true),Convert.FromBase64String(saved.Payload)),saved.Name)
                : fail("adventure sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        var result=name=="GS_COMBINE_PIECE_REQUEST"
            ? CombineCardPieces((GsCombinePieceRequest)request!,p,ok,fail)
            : ClaimFieldReward((GsAchieveRewardRequest)request!,p,ok,fail);
        if(result.Name.EndsWith("_OK")) {
            if(p.AdventureReceipts.Count>=256)p.AdventureReceipts.Remove(p.AdventureReceipts.Keys.First());
            p.AdventureReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,
                TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        return result;
    }

    private Reply StartAdventure(LocalPlayer player, GsNormalBattleStartRequest start,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (LocalStoryChapter.Find((int)start.stageid) is { } chapter)
            return StartStory(player,start,chapter,ok,fail);
        if (start.dungeonFlag > 1)
            return fail("invalid dungeon flag",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (!Enum.IsDefined(start.difficulty) || start.difficulty==BscTable.GAME_DIFFICULTY.DIFFICULTY_MAX)
            return fail("unknown difficulty",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        bool dungeon=start.dungeonFlag==1;
        if(DeckSlots(player,start.deckid==0?(dungeon?2u:1u):start.deckid).Any(x=>InTraining(player,x)))
            return fail("remove training cards from the battle deck",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if (!catalog.BattleStages(start.difficulty,dungeon).TryGetValue(start.stageid, out var stage))
            return fail("normal stage is unsupported or locked",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (dungeon ? !CanEnterDungeon(player,start.difficulty,stage) : !StageUnlock.CanEnter(catalog,player,start.difficulty,start.stageid))
            return fail("normal stage is unsupported or locked",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (stage.RequirePcLevel>0 && player.Level<(uint)stage.RequirePcLevel)
            return fail("player level is too low for this stage",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        if (RemainingBattleCount(player,stage,start.difficulty,dungeon)==0)
            return fail("daily entry limit reached",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if (player.ActiveStage != null) return fail("a local battle is already active",ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS);
        var waves = catalog.WaveIds(start.difficulty, start.stageid, dungeon);
        if(waves.Count==0)return fail("original enemy wave data is missing",ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE);
        uint cost = (uint)Math.Max(stage.ConsumeStamina, 0);
        if (player.Stamina < cost) return fail("not enough local stamina",ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA);
        if(player.Stamina>=catalog.StaminaMax(player.Level)) player.StaminaStamp=Now;
        player.Stamina -= cost;
        LocalBattleAttempts.Bind(player, start.stageid, NextLocalBattleStamp(player), start.difficulty, dungeon);
        if(stage.DailyLimit>0) {var used=BattleCountsOf(player,start.difficulty,dungeon);used[start.stageid]=used.GetValueOrDefault(start.stageid)+1;}
        if (!dungeon) RecordAdventureDeckObjective(player, start.stageid, start.difficulty, start.deckid == 0 ? 1u : start.deckid);
        store.Save();
        var battle = new GsNormalBattleStartOk { stamina = player.Stamina, staminaStamp = player.StaminaStamp, timeStamp = player.ActiveBattleStamp };
        battle.waveProbIdList.AddRange(waves);
        return ok(battle);
    }

    private Reply CombineCardPieces(GsCombinePieceRequest request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var ids=request.itemIdList;
        if(ids.Count is 0 or >300 || ids.Distinct().Count()!=ids.Count)
            return fail("expected a nonempty list of distinct fragment item ids",ServerErrorCode.ERROR_INVALID_ID);
        if(p.ActiveStage!=null)return fail("finish the current battle first",ServerErrorCode.ERROR_INVALID_ID);
        var plan=new List<(BscTable.PieceData Recipe,BscTable.CardData Card)>();
        var identities=catalog.Cards.Where(x=>p.CardIds.Contains((uint)x.Id)).Select(HeroIdentity).ToHashSet();
        foreach(uint id in ids) {
            if (id == LocalStoryChapter.MaknaeFragmentId)
                return fail("请先通关老小篇，碎片用于升星", ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE);
            var recipe=catalog.Pieces.FirstOrDefault(x=>x.ItemId==id);
            var card=recipe==null?null:catalog.UsableHeroes.FirstOrDefault(x=>x.Id==recipe.NextId);
            if(recipe==null || recipe.Complete<=0 || recipe.NextIdType!=BscTable.REWARD_TYPE.REWARD_TYPE_CARD || card==null || card.PieceId!=id ||
                !catalog.Items.Any(x=>x.Id==id && x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE && x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_PIECE_CARD))
                return fail("no usable original card recipe for this fragment",ServerErrorCode.ERROR_DIFFERENCE_PIECE_DATA);
            // Owned characters keep their fragments for evolution. Never consume two
            // pieces just to convert the resulting duplicate back to 10..40 pieces.
            if(!identities.Add(HeroIdentity(card)))return fail("character is already owned; keep these pieces for rank up",ServerErrorCode.ERROR_ALREADY_EXIST);
            if(p.Inventory.GetValueOrDefault(id)<recipe.Complete)
                return fail("not enough fragments for the original recipe",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
            plan.Add((recipe,card));
        }
        if(p.CardIds.Count(x=>x!=0)+plan.Count>300)return fail("card inventory is full",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsCombinePieceOk();
        foreach(var (recipe,card) in plan) {
            uint id=(uint)recipe.ItemId;int left=p.Inventory[id]-recipe.Complete;
            if(left==0)p.Inventory.Remove(id);else p.Inventory[id]=left;
            result.itemInfoList.Add(ItemInfo(id,-recipe.Complete)); // Native job applies a delta, not an absolute count.
            p.CardIds.Add((uint)card.Id);p.CollectedCardIds.Add((uint)card.Id);
            result.cardInfoList.Add(CardInfo((uint)card.Id,(ulong)p.CardIds.Count,p));
        }
        return ok(result);
    }

    private static Dictionary<uint,byte> FieldClaims(LocalPlayer p,BscTable.GAME_DIFFICULTY d)=>d switch {
        BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD=>p.HardFieldRewardClaims,
        BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND=>p.LegendFieldRewardClaims,
        _=>p.NormalFieldRewardClaims
    };
    private byte[] FieldRewardArray(Dictionary<uint,byte> claims)
    {
        int size=Math.Max(256,(catalog.FieldRewards.Values.SelectMany(x=>x).Max(x=>x.FieldId)+1)*2);
        var result=new byte[size];
        foreach(var (field,mask) in claims)if((ulong)field*2<(ulong)size)result[(int)field*2]=mask;
        return result;
    }
    private Reply ClaimFieldReward(GsAchieveRewardRequest request,LocalPlayer p,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_INVALID_ACHIEVEMENT_REWARD_INFO);
        if(!catalog.FieldRewards.ContainsKey(request.difficulty) || request.fieldID>int.MaxValue || request.rewardID is 0 or >7)return Invalid("invalid difficulty, field or reward ordinal");
        var field=catalog.FieldRewards[request.difficulty].FirstOrDefault(x=>x.FieldId==request.fieldID);
        if(field==null || request.rewardID>field.List.Count || !catalog.NormalFieldStages.TryGetValue((int)request.fieldID,out var stages))return Invalid("original field reward not found");
        var reward=field.List[(int)request.rewardID-1];
        var claims=FieldClaims(p,request.difficulty);byte mask=(byte)(1<<(int)request.rewardID);
        if((claims.GetValueOrDefault(request.fieldID)&mask)!=0)return fail("field reward already claimed",ServerErrorCode.ERROR_ALREADY_RECEIVE_ACHEIVE_REWARD);
        if(!StageUnlock.FieldOpen(catalog,p,request.difficulty,(int)request.fieldID))return Invalid("field is not unlocked");
        var records=StageRecordsOf(p,request.difficulty);
        int stars=stages.Where(id=>catalog.StagesFor(request.difficulty).ContainsKey(id))
            .Sum(id=>(int)Math.Min((byte)3,records.GetValueOrDefault(id)?.Stars??0));
        // FieldSceneManager.GetFieldAchievedStar includes hidden dungeons only in hard mode.
        if(request.difficulty==BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD)
            stars+=catalog.Dungeons[request.difficulty].Values.Where(x=>x.FieldId==request.fieldID)
                .Sum(x=>(int)Math.Min((byte)3,p.HardDungeonStageRecords.GetValueOrDefault((uint)x.Id)?.Stars??0));
        if(reward.StarCount<=0 || stars<reward.StarCount)return Invalid("not enough earned stars in this difficulty");
        int count=reward.RewardValueId.Count;
        if(count==0 || count!=reward.RewardValueIdType.Count || count!=reward.Quantity.Count)return Invalid("malformed original reward row");
        var grant=new ShopGrant();
        for(int i=0;i<count;i++)if(!AddShopReward(p,grant,reward.RewardValueIdType[i],reward.RewardValueId[i],reward.Quantity[i]))return Invalid("unsupported original reward");
        if(grant.Stamina!=0)return Invalid("field reward contains unsupported stamina"); // This APK has no stamina field in the OK packet.
        if(!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0))return fail("reward exceeds inventory or currency capacity",ServerErrorCode.ERROR_OVER_MAX_COUNT);
        var result=new GsAchieveRewardOk {fieldID=request.fieldID,rewardID=request.rewardID,addGold=(uint)grant.Gold,addCashPoint=(uint)grant.Cash,addCardCount=(uint)grant.Cards.Count};
        ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_GOLD,0,result.addItemInfoList,result.addCardInfoList);
        claims[request.fieldID]=(byte)(claims.GetValueOrDefault(request.fieldID)|mask);
        result.currentCoinInfo=Coins(p);
        return ok(result);
    }
}
