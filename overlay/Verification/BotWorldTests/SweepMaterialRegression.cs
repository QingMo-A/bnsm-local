using System.Text.Json;

internal static class SweepMaterialRegression
{
    internal static async Task Run(string root)
    {
        string folder="I:/BnsmModernWork/Verification/sweep-material-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);
        var checks=new List<string>();var samples=new List<object>();
        void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;var resource=new BoardResourceCatalog(root);
        p.Level=99;p.VipExp=1000000;p.ClearStage=c.Stages.Keys.Max();p.ClearHardStage=c.HardStages.Keys.Max();p.ClearLegendStage=c.LegendStages.Keys.Max();
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        var groups=c.DropGroups.ToDictionary(x=>x.Id);
        var attendance=new AttendanceRules(root);
        HashSet<uint> Possible(IReadOnlyList<BscTable.NPC_REWARD_TYPE> types,IReadOnlyList<int> ids,HashSet<int>? ancestors=null){
            ancestors??=[];var idsOut=new HashSet<uint>();
            for(int i=0;i<ids.Count;i++)if(types[i]==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM)idsOut.Add((uint)ids[i]);
            else if(types[i]==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM){
                Check(ancestors.Add(ids[i])&&groups.ContainsKey(ids[i]),"Valid acyclic original drop group "+ids[i]);
                var g=groups[ids[i]];idsOut.UnionWith(Possible(g.NpcRewardIdType,g.NpcRewardId,ancestors));ancestors.Remove(ids[i]);
            }
            return idsOut;
        }
        int sources=0;foreach(var item in c.Items)foreach(var source in item.Source.Where(s=>s.linkId>0))foreach(int diff in source.difficult.Count==0?new List<int>{0}:source.difficult){
            HashSet<uint>? pool=null;
            if(source.type==BscTable.LINK_TYPE.LINK_FIELD&&c.BattleStages((BscTable.GAME_DIFFICULTY)diff,source.dungeon).TryGetValue((uint)source.linkId,out var row))pool=Possible(row.NpcRewardIdType,row.NpcRewardId);
            else if(source.type is BscTable.LINK_TYPE.LINK_RESOURCEDUNGEON or BscTable.LINK_TYPE.LINK_SOKSUNG&&diff<resource.Rewards.Length){var reward=resource.Rewards[diff].SingleOrDefault(x=>x.StageId==source.linkId);if(reward!=null)pool=Possible(reward.NpcRewardIdType,reward.NpcRewardId);}
            if(pool!=null){Check(pool.Contains((uint)item.Id),$"Material source {item.Id}/{source.type}/{source.linkId}/{diff}/{source.dungeon} is obtainable");sources++;}
        }
        string State()=>JsonSerializer.Serialize(new{p.Inventory,p.EquipmentItems,p.Stamina,p.Gold,p.CashPoint,p.Missions,p.Resources,p.LootReceipts,p.ResourceReceipts});
        void Fresh(){p.Inventory.Clear();p.EquipmentItems.Clear();p.Stamina=100000;p.Gold=1000000;p.Exp=(uint)c.PlayerStates.Single(x=>x.Level==99).Exp;p.HardBattleCounts.Clear();p.LegendBattleCounts.Clear();p.HardDungeonBattleCounts.Clear();}
        void Verify(IReadOnlyList<PKBattleSkipReward> pages,PKUserItem? expItem,IReadOnlyList<BscTable.NPC_REWARD_TYPE> types,IReadOnlyList<int> ids,IReadOnlyList<int> quantities,uint count,string label,Func<int,bool>? guaranteed=null){
            var delta=pages.SelectMany(x=>x.earnedItemList).Concat(expItem==null?[]:new[]{expItem}).Where(x=>x.itemCount!=0).ToList();
            var stacked=delta.Where(x=>c.ItemsById[(int)x.itemID].ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).GroupBy(x=>x.itemID).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.itemCount));
            Check(p.Inventory.Count==stacked.Count&&stacked.All(x=>p.Inventory.GetValueOrDefault(x.Key)==x.Value),label+" packet item totals equal saved inventory");
            var equipment=delta.Where(x=>c.ItemsById[(int)x.itemID].ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).ToList();
            Check(equipment.Count==p.EquipmentItems.Count&&equipment.All(x=>x.itemCount==1&&p.EquipmentItems.Any(y=>y.Sequence==x.itemSeq&&y.ItemId==x.itemID)),label+" packet equipment instances equal saved inventory");
            for(int i=0;i<ids.Count;i++)if(types[i]==BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM&&(guaranteed==null||guaranteed(i)))Check(delta.Where(x=>x.itemID==ids[i]).Sum(x=>x.itemCount)>=quantities[i]*count,label+" delivers direct preview reward "+ids[i]);
            samples.Add(new{label,count,items=stacked,equipment=equipment.Count,payloadBytes=peer.LastPayload.Length});
        }
        int stages=0,resources=0,wander=0;
        foreach(var diff in new[]{BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND})foreach(bool dungeon in new[]{false,true})foreach(var row in c.BattleStages(diff,dungeon).Values){
            if(row.Id<=GameCatalog.TutorialEndStage&&!dungeon)continue;
            if(!dungeon&&!StageUnlock.CanEnter(c,p,diff,(uint)row.Id)||dungeon&&!StageUnlock.FieldOpen(c,p,diff,row.FieldId))continue;
            Fresh();var records=dungeon?(diff==0?p.DungeonStageRecords:diff==(BscTable.GAME_DIFFICULTY)1?p.HardDungeonStageRecords:p.LegendDungeonStageRecords):(diff==0?p.StageRecords:diff==(BscTable.GAME_DIFFICULTY)1?p.HardStageRecords:p.LegendStageRecords);records[(uint)row.Id]=new(){Stars=3};
            uint count=(uint)(row.DailyLimit>0?Math.Min(row.DailyLimit,3):10);var request=new GsNormalBattleSkipRequest{stageId=(uint)row.Id,difficulty=diff,dungeonFlag=dungeon,skipCount=count};
            var result=await peer.Request<GsNormalBattleSkipOk>(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,request);var payload=peer.LastPayload.ToArray();
            string label=$"Sweep {diff}/{dungeon}/{row.Id}";
            Check(result.earnedList.Count==count&&p.Stamina==100000-row.ConsumeStamina*count,label+" exact run count and stamina");
            int multiplier=attendance.AdventureDropChanceMultiplier(diff,dungeon,ProtocolRouter.Now);
            Verify(result.earnedList,result.deltaExpItemInfo,row.NpcRewardIdType,row.NpcRewardId,row.RewardQuantity,count,label,i=>{
                double chance=row.DropProb.Count==0?LocalAdventureDropRules.BaseChance(row.NpcRewardIdType[i],c.ItemsById[row.NpcRewardId[i]].ItemType):row.DropProb[i];
                return LocalAdventureDropRules.EffectiveChance(chance,multiplier)>=100;
            });
            string state=State();await peer.Request<GsNormalBattleSkipOk>(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,request,retry:true);
            Check(State()==state&&peer.LastPayload.SequenceEqual(payload),label+" retry preserves exact drops without charging twice");stages++;
        }
        foreach(var row in resource.Resources.Where(x=>runtime.Router.ResourceOpen(x,ProtocolRouter.Now)))for(int d=0;d<resource.Rewards.Length;d++){
            Fresh();p.Resources.Used[(uint)row.Id]=0;p.Resources.Cleared.Add((uint)(row.Id*10+d));uint count=(uint)Math.Min(row.EnterCount,3);
            var request=new GsResourceStageSkipRequest{stageId=(uint)row.Id,difficulty=(BscTable.RESOURCE_DIFFICULTY)d,skipCount=count};
            var result=await peer.Request<GsResourceStageSkipOk>(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,request);var payload=peer.LastPayload.ToArray();var reward=resource.Rewards[d].Single(x=>x.StageId==row.Id);
            string label=$"Resource sweep {row.Id}/{d}";Check(result.earnedList.Count==count&&p.Resources.Used[(uint)row.Id]==count&&p.Stamina==100000-row.ConsumeStamina*count,label+" exact run count, entries and stamina");
            Verify(result.earnedList,result.deltaExpItemInfo,reward.NpcRewardIdType,reward.NpcRewardId,reward.RewardQuantity,count,label);
            string state=State();await peer.Request<GsResourceStageSkipOk>(GameServerCommand.GS_RESOURCE_STAGE_SKIP_REQUEST,request,retry:true);Check(State()==state&&peer.LastPayload.SequenceEqual(payload),label+" retry keeps exact drops and entries");resources++;
        }
        var raids=new RaidCatalog(root);
        foreach(var (difficulty,rows) in raids.Wander){
            Fresh();p.Wander=new(){Day=(ProtocolRouter.Now+8*3600)/86400,Difficulty=difficulty,Spot=1,BestSpots=[(uint)raids.Wander[1].Max(x=>x.Id),(uint)raids.Wander[2].Max(x=>x.Id),(uint)raids.Wander[3].Max(x=>x.Id)]};
            var request=new GsClearWanderBattleMapRequest();var result=await peer.Request<GsClearWanderBattleMapOk>(GameServerCommand.GS_CLEAR_WANDER_BATTLE_MAP_REQUEST,request);var payload=peer.LastPayload.ToArray();
            var delta=result.earnedItemList.GroupBy(x=>x.itemID).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.itemCount));string label="Wander sweep "+difficulty;
            Check(p.Inventory.Count==delta.Count&&delta.All(x=>p.Inventory.GetValueOrDefault(x.Key)==x.Value),label+" packet materials equal saved inventory");
            var pool=new HashSet<uint>();foreach(var row in rows.Where(x=>x.Type==BscTable.WanderSpotDataType.Type_Reward))pool.UnionWith(Possible(row.NpcRewardIdType,row.NpcRewardId));
            Check(delta.Keys.All(pool.Contains)&&delta.Values.All(x=>x>0)&&delta.Count>0,label+" gives original ordinary checkpoint drops");
            Check(result.earnedCoinInfo.gold==rows.Where(x=>x.Type==BscTable.WanderSpotDataType.Type_Reward).Sum(x=>x.Gold),label+" grants checkpoint gold exactly once");
            string state=State();await peer.Request<GsClearWanderBattleMapOk>(GameServerCommand.GS_CLEAR_WANDER_BATTLE_MAP_REQUEST,request,retry:true);Check(State()==state&&peer.LastPayload.SequenceEqual(payload),label+" retry cannot reroll or duplicate materials");
            samples.Add(new{label,items=delta,payloadBytes=payload.Length});wander++;
        }
        runtime.Store.Save();var saved=new PlayerStore(Path.Combine(folder,"saves"),c).Find(p.Uid)!;Check(JsonSerializer.Serialize(saved.Inventory)==JsonSerializer.Serialize(p.Inventory),"Sweep materials survive save reload");
        string serverCoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/sweep-material-latest.json",JsonSerializer.Serialize(new{passed=true,sources,stages,resources,wander,checks,samples,fixture=folder,productionSaveUsed=false,serverCoreSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"SWEEP_MATERIAL_PASSED checks={checks.Count} sources={sources} stages={stages} resources={resources} wander={wander}");
    }
    public static void Audit(string root)
    {
        var c=new GameCatalog(root);var resource=new BoardResourceCatalog(root);
        var stages=(from diff in new[]{BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND}
                    from dungeon in new[]{false,true} from stage in c.BattleStages(diff,dungeon).Values
                    select new{diff,dungeon,stage}).ToArray();
        File.WriteAllText("I:/BnsmModernWork/Verification/sweep-material-catalog.json",JsonSerializer.Serialize(new{
            stages,resources=resource.Resources,rewards=resource.Rewards,groups=c.DropGroups,wander=new RaidCatalog(root).Wander,
            items=c.Items.Where(x=>x.Source.Count>0).Select(x=>new{x.Id,x.IdAlias,x.NameAlias,x.ItemType,x.Source})
        },new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        Console.WriteLine("Wrote sweep material catalog audit");
    }
}
