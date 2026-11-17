using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    private Reply? HandleCrafting(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_EQUIPMENT_CRAFT_REQUEST" or "GS_EQUIPMENT_AWAKE_REQUEST" or "GS_GEM_CRAFT_REQUEST" or "GS_ITEM_CRAFT_REQUEST" or "GS_HELPER_COSTUME_CRAFT_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.CraftingReceipts.TryGetValue(key,out var prior))return prior.Fingerprint==fingerprint
            ?new(prior.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(prior.TypeName,true),Convert.FromBase64String(prior.Payload)),prior.Name)
            :fail("crafting sequence reused with different parameters",ServerErrorCode.ERROR_INVALID_ID);
        if(p.ActiveStage!=null)return fail("finish the current battle before crafting",ServerErrorCode.ERROR_INVALID_ID);
        var result=request switch {
            GsEquipmentCraftRequest r=>CraftEquipment(p,r.cardSeq,r.baseEquipmentSeq,r.targetEquipmentId,false,ok,fail),
            GsEquipmentAwakeRequest r=>CraftEquipment(p,r.cardSeq,r.baseEquipmentSeq,r.targetEquipmentId,true,ok,fail),
            GsGemCraftRequest r=>CraftGem(p,r,ok,fail),
            GsItemCraftRequest r=>CraftIngredient(p,r.targetItemId,ok,fail),
            GsHelperCostumeCraftRequest r=>CraftHelperCostume(p,r,ok,fail),
            _=>fail("invalid crafting request",ServerErrorCode.ERROR_INVALID_ID)
        };
        if(result.Name.EndsWith("_OK")) {
            if(p.CraftingReceipts.Count>=256)p.CraftingReceipts.Remove(p.CraftingReceipts.Keys.First());
            p.CraftingReceipts[key]=new() {Fingerprint=fingerprint,Command=result.Command,Name=result.Name,TypeName=result.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(result.Message))};
            store.Save();
        }
        return result;
    }

    private Reply CraftHelperCostume(LocalPlayer p,GsHelperCostumeCraftRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        var source=catalog.ItemsById.GetValueOrDefault((int)r.baseItemId);
        var target=catalog.ItemsById.GetValueOrDefault((int)r.targetItemId);
        if(source?.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_CLOTHES||target?.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_CLOTHES||
            source.Next1Id==0||source.Next1Id!=r.targetItemId||target.EnchantValue!=source.EnchantValue+1||target.EnchantValue>10)
            return fail("服装已达强化上限，或所选强化路线无效。",ServerErrorCode.ERROR_INVALID_CRAFT_REQUEST);
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_CLOTHES_ENCHANT))
            return fail("当前玩家等级尚未解锁服装强化。",ServerErrorCode.ERROR_INVALID_USER_LEVEL);
        if(p.Inventory.GetValueOrDefault(r.baseItemId)<=0)return fail("所选服装已不在背包中，请刷新后重试。",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        var rows=catalog.CardProcesses.List.Single(x=>x.ProcessType==BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_CLOTHES_ENCHANT).ProcessDataList;
        int index=target.EnchantValue-1;
        if(index<0||index>=rows.Count||rows[index].Probability!=100||rows[index].CostGold<0||
            source.Material1Id.Count==0||source.Material1Id.Count!=source.Material1Value.Count)
            return fail("服装强化配置不完整。",ServerErrorCode.ERROR_INVALID_CRAFT_REQUEST);
        // Native ClothesItem.GetItemMaterial adds the selected garment ON TOP
        // of the source row's duplicate base outfits and fabric. Equipped state
        // is a reference to this shared stack, not three separate garments.
        var materials=new Dictionary<uint,long>{{r.baseItemId,1}};
        for(int i=0;i<source.Material1Id.Count;i++) {
            int id=source.Material1Id[i],count=source.Material1Value[i];
            if(id<=0||count<=0||!catalog.ItemsById.TryGetValue(id,out var material)||
                material.ItemType is not (BscTable.ITEM_TYPE.ITEM_TYPE_CLOTHES or BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT))
                return fail("服装强化材料配置无效。",ServerErrorCode.ERROR_INVALID_CRAFT_REQUEST);
            materials[(uint)id]=materials.GetValueOrDefault((uint)id)+count;
        }
        var plan=new CraftPlan(p,catalog);
        if(!plan.Cost(rows[index].CostGold))return fail(plan.Error,plan.Code);
        foreach(var material in materials)if(!plan.Consume(material.Key,material.Value,catalog.ItemsById[(int)material.Key].ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT))
            return fail("服装强化所需的同款服装或布料不足。",ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT);
        if(!plan.Add(r.targetItemId,1))return fail(plan.Error,plan.Code);
        var changes=plan.Commit();
        foreach(uint helper in p.HelperCostumes.Where(x=>x.Value==r.baseItemId||
            (materials.ContainsKey(x.Value)&&p.Inventory.GetValueOrDefault(x.Value)==0)).Select(x=>x.Key).ToArray()) {
            var data=catalog.Helpers.Single(x=>(uint)x.HelperType==helper);
            p.HelperCostumes[helper]=data.FindModel((int)r.targetItemId)!=null?r.targetItemId:(uint)data.DefaultId;
        }
        var result=new GsHelperCostumeCraftOk{result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),nameCardInfo=NameCard(p)};
        // Put the crafted garment first: the native result dialog chooses the
        // first positive delta, not the last recursively crafted ingredient.
        result.userItemList.AddRange(changes.OrderByDescending(x=>x.itemID==r.targetItemId));result.helperInfoList.AddRange(HelperList(p));return ok(result);
    }

    // Plan against a private inventory. Recursive ingredient recipes must finish
    // before any coins, stack counts, equipment instances or sockets are changed.
    private sealed class CraftPlan(LocalPlayer player,GameCatalog catalog,bool checkFunds=true)
    {
        public readonly Dictionary<uint,int> Inventory=new(player.Inventory);
        public long Gold;
        public string Error="not enough crafting materials";
        public ServerErrorCode Code=ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT;
        private readonly HashSet<uint> path=[];
        public bool Add(uint id,long count)
        {
            long total=Inventory.GetValueOrDefault(id)+count;
            if(total<0||total>int.MaxValue) {Error="crafting inventory capacity exceeded";Code=ServerErrorCode.ERROR_OVER_MAX_COUNT;return false;}
            if(total==0)Inventory.Remove(id);else Inventory[id]=(int)total;
            return true;
        }
        public bool Cost(long cost)
        {
            if(cost<0||cost>uint.MaxValue-Gold||(checkFunds&&Gold+cost>player.Gold)) {Error="not enough gold for crafting and ingredient recipes";Code=ServerErrorCode.ERROR_NOT_ENOUGH_GOLD;return false;}
            Gold+=cost;return true;
        }
        public bool Consume(uint id,long count,bool recursive)
        {
            if(count<0||count>int.MaxValue)return false;
            if(count==0)return true;
            long have=Inventory.GetValueOrDefault(id);
            if(have<count&&(!recursive||!Produce(id,count-have)))return false;
            return Add(id,-count);
        }
        public bool Produce(uint id,long count)
        {
            var item=catalog.Items.FirstOrDefault(x=>x.Id==id);
            if(item==null||count<=0||count>int.MaxValue||path.Count>=32||!path.Add(id))return false;
            try {
                var materials=new Dictionary<uint,long>();long yield=1,cost;
                if(item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT) {
                    var recipe=catalog.Recipes.FirstOrDefault(x=>x.TargetItemId==id);
                    if(recipe==null||recipe.TargetItemQuantity<=0||recipe.MeterialItemId.Count==0||recipe.MeterialItemId.Count!=recipe.MeterialItemQuantity.Count||recipe.Cost<0||
                        (recipe.Cost!=0&&recipe.CostType!=BscTable.MONEY_TYPE.MONEY_TYPE_GOLD))return false;
                    yield=recipe.TargetItemQuantity;cost=recipe.Cost;
                    for(int i=0;i<recipe.MeterialItemId.Count;i++){if(recipe.MeterialItemId[i]<=0||recipe.MeterialItemQuantity[i]<=0)return false;uint key=(uint)recipe.MeterialItemId[i];materials[key]=materials.GetValueOrDefault(key)+recipe.MeterialItemQuantity[i];}
                } else if(item.ItemType is BscTable.ITEM_TYPE.ITEM_TYPE_GEM or BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE) {
                    bool gem=item.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM;
                    var source=catalog.Items.FirstOrDefault(x=>x.ItemType==item.ItemType&&x.Next1Id==id);
                    var gate=gem?BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_GEM:BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_EMPOWER;
                    var limit=gem?BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_GEM:BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_EMPOWER;
                    if(source==null||player.Level<catalog.ContentValue(gate)||(gem?source.Step:source.Rank)>=catalog.ContentValue(limit)||source.Material1Id.Count==0||source.Material1Id.Count!=source.Material1Value.Count)return false;
                    var rows=catalog.CardProcesses.List.First(x=>x.ProcessType==(gem?BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_GEM_CRAFT:BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EMPOWER_STONE_CRAFT)).ProcessDataList;
                    int index=gem?item.Step-1:Math.Clamp(item.Rank-1,0,9);if(index<0||index>=rows.Count||rows[index].Probability!=100)return false;cost=rows[index].CostGold;
                    materials[(uint)source.Id]=1;
                    for(int i=0;i<source.Material1Id.Count;i++){if(source.Material1Id[i]<=0||source.Material1Value[i]<=0)return false;uint key=(uint)source.Material1Id[i];materials[key]=materials.GetValueOrDefault(key)+source.Material1Value[i];}
                } else return false;
                long batches=(count+yield-1)/yield;
                if(!Cost(cost*batches))return false;
                foreach(var pair in materials)if(!Consume(pair.Key,pair.Value*batches,true))return false;
                return Add(id,batches*yield);
            } finally {path.Remove(id);}
        }
        public List<PKUserItem> Commit()
        {
            // ConvertItemList treats every nonzero serial as the upgraded
            // equipment. Stack deltas in crafting responses MUST have serial 0.
            var changes=player.Inventory.Keys.Union(Inventory.Keys).Order().Select(id=>new PKUserItem {itemID=id,itemCount=Inventory.GetValueOrDefault(id)-player.Inventory.GetValueOrDefault(id)})
                .Where(x=>x.itemCount!=0).ToList();
            player.Inventory=Inventory;player.Gold-=(uint)Gold;return changes;
        }
    }

    private BscTable.ProcessData? CraftProcess(BscTable.ItemData target,bool awake=false)
    {
        var type=target.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM?BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_GEM_CRAFT:
            awake?target.EquipmentType switch {
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME=>BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_AWAKNING1,
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_MORE_EXTREME=>BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_AWAKNING2,
                _=>(BscTable.ITEM_CARD_PROCESS_TYPE)(-1)}:
            target.EquipmentType switch {
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL=>BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_CRAFT_NORMAL,
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME=>BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_CRAFT_EXTREME,
                BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_MORE_EXTREME=>BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_CRAFT_MORE_EXTREME,
                _=>(BscTable.ITEM_CARD_PROCESS_TYPE)(-1)};
        var rows=catalog.CardProcesses.List.FirstOrDefault(x=>x.ProcessType==type)?.ProcessDataList;
        return target.Step>0&&target.Step<=rows?.Count?rows[target.Step-1]:null;
    }
    private bool CraftMaterials(CraftPlan plan,List<int> ids,List<int> quantities,uint count=1,bool gem=false)
    {
        if(ids.Count==0||ids.Count!=quantities.Count)return false;
        for(int i=0;i<ids.Count;i++) {
            var item=catalog.Items.FirstOrDefault(x=>x.Id==ids[i]);
            // GemItem.GetItemMaterial adds the selected base gem to row zero.
            long quantity=(long)quantities[i]+(gem&&i==0?1:0);
            if(ids[i]<=0||quantities[i]<=0||item==null||
                (item.ItemType is not (BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT or BscTable.ITEM_TYPE.ITEM_TYPE_PIECE)&&(!gem||item.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_GEM))||
                !plan.Consume((uint)ids[i],quantity*count,true))return false;
        }
        return true;
    }
    private PKUserCardInfo CraftCardInfo(LocalPlayer p,ulong seq)=>seq==0?new PKUserCardInfo {cardSeq=0}:CardInfo(p.CardIds[(int)seq-1],seq,p);
    private Reply CraftEquipment(LocalPlayer p,ulong cardSeq,ulong seq,uint targetId,bool awake,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_INVALID_ID);
        var owned=p.EquipmentItems.FirstOrDefault(x=>x.Sequence==seq);
        var source=owned==null?null:catalog.Items.FirstOrDefault(x=>x.Id==owned.ItemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT);
        var target=catalog.Items.FirstOrDefault(x=>x.Id==targetId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT);
        if(source==null||target==null||targetId==0||(awake?source.AwakningNextId:source.Next1Id)!=targetId)return Invalid("equipment does not have this upgrade path");
        if (catalog.StoryForgeSources.TryGetValue(source.Id, out var chapter) &&
            (!p.StoryClears.Contains((uint)chapter.Id) && !ClaimedLegacyHongForge(p, chapter) ||
                chapter.RequiredHero != 0 && !HasStoryHero(p, chapter.RequiredHero)))
            return Invalid("先完成「" + chapter.Title + "」" +
                (chapter.RequiredHero == 0 ? "" : "并持有" + chapter.RequiredHeroName));
        if(EquipmentOwner(p,seq)!=cardSeq)return Invalid("equipment wearer does not match the request");
        if(cardSeq!=0&&InTraining(p,cardSeq))return fail("finish training before changing this equipment",ServerErrorCode.ERROR_CARD_IN_TRAINING);
        if(cardSeq!=0) {
            if(!Owns(p,cardSeq))return Invalid("unknown equipment wearer");
            var card=catalog.RequireCard(p.CardIds[(int)cardSeq-1]);
            var state=p.CardStates.GetValueOrDefault(cardSeq)??new LocalCardState();
            if(!FitsEquipment(target,card,state.Level))return Invalid("upgraded equipment does not fit the wearer's identity or level");
        }
        var gate=awake?source.EquipmentType switch {
            BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_AWAKE_EXTREME,
            BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_AWAKE_MORE_EXTREME,
            _=>BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_CHANGE}:BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_EQUIPMENT;
        if(!FeatureOpen(p,gate))return Invalid("equipment crafting or awakening is not unlocked at this player level");
        var process=CraftProcess(target,awake);
        if(process==null||process.Probability!=100)return Invalid("unsupported equipment process configuration");
        if((p.DebugFlags&(awake?LocalDebugFlags.FreeEquipmentAwake:LocalDebugFlags.FreeEquipmentCraft))!=0) {
            target=DebugEquipmentTarget(p,source,target,awake,cardSeq);
            owned!.ItemId=(uint)target.Id;
            var changes=new List<PKUserItem>{EquipmentInfo(p,owned)};
            if(awake){var response=new GsEquipmentAwakeOk{result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,cardSeq)};response.userItemList.AddRange(changes);return ok(response);}
            else {var response=new GsEquipmentCraftOk{result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,cardSeq)};response.userItemList.AddRange(changes);return ok(response);}
        }
        var plan=new CraftPlan(p,catalog);
        if(!plan.Cost(target.Rank==0?0:process.CostGold)||!CraftMaterials(plan,awake?source.AwakningMaterialId:source.Material1Id,awake?source.AwakningMaterialValue:source.Material1Value))return fail(plan.Error,plan.Code);
        var items=plan.Commit();
        // Same unique instance, wearer and registration time survive both paths.
        // The APK converts this single row to old-ID -1 / new-ID +1 locally.
        owned!.ItemId=targetId;items.Add(EquipmentInfo(p,owned));
        if(awake) {var result=new GsEquipmentAwakeOk {result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,cardSeq)};result.userItemList.AddRange(items);return ok(result);}
        else {var result=new GsEquipmentCraftOk {result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,cardSeq)};result.userItemList.AddRange(items);return ok(result);}
    }
    private Reply CraftGem(LocalPlayer p,GsGemCraftRequest r,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        Reply Invalid(string reason)=>fail(reason,ServerErrorCode.ERROR_INVALID_ID);
        var source=catalog.Items.FirstOrDefault(x=>x.Id==r.baseGemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM);
        var target=catalog.Items.FirstOrDefault(x=>x.Id==r.targetGemId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM);
        if(source==null||target==null||source.Next1Id!=r.targetGemId||r.craftCount>999)return Invalid("invalid gem recipe or batch size");
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_GEM)||source.Step>=catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_GEM))return Invalid("gem crafting is not unlocked or the version limit has been reached");
        // Native socket dialog sends craftCount=0 for its one-item operation;
        // GameSocket converts UI slots 3..5 to wire indexes 0..2. Its unsigned
        // clamp also sends index 2 for bag crafting; that index has no wearer.
        uint count=r.cardSeq==0?r.craftCount:1;
        LocalCardState? state=null;
        if(r.cardSeq==0) {if(r.slotIndex>2||count==0)return Invalid("invalid inventory gem batch");}
        else {
            if(!Owns(p,r.cardSeq)||r.slotIndex>2||r.craftCount>1)return Invalid("invalid gem socket");
            if(InTraining(p,r.cardSeq))return fail("finish training before changing this socket",ServerErrorCode.ERROR_CARD_IN_TRAINING);
            state=p.CardStates.GetValueOrDefault(r.cardSeq);
            if(state==null||state.GemIds[(int)r.slotIndex]!=r.baseGemId)return Invalid("selected gem is not in this socket");
        }
        var process=CraftProcess(target);
        if(process==null||process.Probability!=100)return Invalid("unsupported gem process configuration");
        if((p.DebugFlags&LocalDebugFlags.FreeGemCraft)!=0) {
            target=DebugStackTarget(p,source,target,true);
            var debugPlan=new CraftPlan(p,catalog);
            // The owned base is transformed, not duplicated. Catalysts and coins are free.
            if(state==null&&(!debugPlan.Consume(r.baseGemId,count,false)||!debugPlan.Add((uint)target.Id,count)))return fail(debugPlan.Error,debugPlan.Code);
            var changes=debugPlan.Commit();
            if(state!=null)state.GemIds[(int)r.slotIndex]=(uint)target.Id;
            var response=new GsGemCraftOk{result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,r.cardSeq)};
            response.userItemList.AddRange(changes);return ok(response);
        }
        var plan=new CraftPlan(p,catalog);
        // Temporarily count the selected worn gem as one material, without
        // touching any other socket or charging an unwear fee.
        if(state!=null&&!plan.Add(r.baseGemId,1))return fail(plan.Error,plan.Code);
        if(!plan.Cost((long)process.CostGold*count)||!CraftMaterials(plan,source.Material1Id,source.Material1Value,count,true))return fail(plan.Error,plan.Code);
        if(state==null&&!plan.Add(r.targetGemId,count))return fail(plan.Error,plan.Code);
        var items=plan.Commit();
        if(state!=null)state.GemIds[(int)r.slotIndex]=r.targetGemId;
        var result=new GsGemCraftOk {result=CraftResult.CRAFT_RESULT_SUCCESS,nowCoinInfo=Coins(p),changedCardInfo=CraftCardInfo(p,r.cardSeq)};
        result.userItemList.AddRange(items);return ok(result);
    }
    private Reply CraftIngredient(LocalPlayer p,uint targetId,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!FeatureOpen(p,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ITEM_CRAFT_EQUIPMENT))return fail("ingredient crafting is not unlocked",ServerErrorCode.ERROR_INVALID_ID);
        var plan=new CraftPlan(p,catalog);
        if(!plan.Produce(targetId,1))return fail(plan.Error,plan.Code);
        var result=new GsItemCraftOk {result=CraftResult.CRAFT_RESULT_SUCCESS};result.userItemList.AddRange(plan.Commit());result.nowCoinInfo=Coins(p);return ok(result);
    }
}
