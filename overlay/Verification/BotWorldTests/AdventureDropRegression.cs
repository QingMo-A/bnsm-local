using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class AdventureDropRegression
{
    internal static async Task Run(string root)
    {
        var folder=Path.Combine(root,"..","Verification","adventure-drop-"+Guid.NewGuid().ToString("N"));
        string save=Path.Combine(folder,"saves");
        await using var runtime=new LocalRuntime(root,save,0,0,0,false);
        FactionRegression.Prepare(runtime);
        var p=runtime.Store.All().Single();var catalog=runtime.Catalog;
        p.Level=99;p.VipExp=1000000;p.ClearStage=catalog.Stages.Keys.Max();p.ClearHardStage=catalog.HardStages.Keys.Max();p.ClearLegendStage=catalog.LegendStages.Keys.Max();
        using var peer=new Peer(root);await peer.Login(runtime,p.Account);
        var rules=(AttendanceRules)typeof(ProtocolRouter).GetProperty("Attendance",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime.Router)!;
        var normal=rules.Operations.Single(x=>x.Type=="NORMAL_BATTLE_ITEM_DROP");
        var checks=new List<string>();
        void Check(bool passed,string name){if(!passed)throw new InvalidDataException(name);checks.Add(name);}
        const BscTable.GAME_DIFFICULTY Normal=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL;
        const BscTable.NPC_REWARD_TYPE Item=BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM,Group=BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_GROUP_ITEM;
        const int Material=260000001,GroupMaterial=260000002,NoDrop=260000003,Exp=210002001,Piece=205060321,Gem=120201003;
        const BscTable.GAME_DIFFICULTY Hard=BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD;
        double GemChance()=>LocalAdventureDropRules.BaseChance(Item,BscTable.ITEM_TYPE.ITEM_TYPE_GEM);
        double Chance(double baseChance,int boost)=>LocalAdventureDropRules.EffectiveChance(baseChance,boost);
        Check(GemChance()==0.2&&Chance(GemChance(),150)==30&&Chance(GemChance(),1)==0.2,"Optional gem fallback rises from 0.2% to 30%");
        Check(Chance(1,150)==100&&Chance(0,150)==0&&Chance(100,150)==100,"Probability is capped and preserves impossible and guaranteed rows");
        string State()=>JsonSerializer.Serialize(new{p.Inventory,p.Stamina,p.Gold,p.Exp,p.Missions,p.ActiveStage,p.LootReceipts,p.HardBattleCounts,p.LegendBattleCounts});
        async Task<bool> Advertises(int value)=> (await peer.Request<GsGetOperationEventDataOk>(GameServerCommand.GS_GET_OPERATION_EVENT_DATA_REQUEST,new GsGetOperationEventDataRequest())).operationDataList.Any(x=>x.Id==normal.Id&&x.Value==value);
        Check(await Advertises(150),"The published operation advertises 150");
        // Only isolated catalog rows are modified to force deterministic wins,
        // a failed roll and a group whose first-clear draw is not multiplied.
        var group=catalog.DropGroups.First();group.NpcRewardId.Clear();group.NpcRewardId.Add(GroupMaterial);
        group.NpcRewardIdType.Clear();group.NpcRewardIdType.Add(Item);group.Quantity.Clear();group.Quantity.Add(2);group.DropProb.Clear();
        void Prepare(BscTable.BattleStageLink row){
            row.NpcRewardId.Clear();row.NpcRewardId.AddRange([Material,group.Id,NoDrop,Exp,Piece,Gem]);
            row.NpcRewardIdType.Clear();row.NpcRewardIdType.AddRange([Item,Group,Item,Item,Item,Item]);
            row.RewardQuantity.Clear();row.RewardQuantity.AddRange([2,1,1,4,1,1]);
            row.DropProb.Clear();row.DropProb.AddRange([1,1,0,100,100,1]);
            row.FirstNpcRewardId.Clear();row.FirstNpcRewardId.AddRange([Material,group.Id,NoDrop,Gem]);
            row.FirstNpcRewardIdType.Clear();row.FirstNpcRewardIdType.AddRange([Item,Group,Item,Item]);
            row.FirstRewardQuantity.Clear();row.FirstRewardQuantity.AddRange([5,2,3,9]);row.FirstDropProb.Clear();row.FirstDropProb.AddRange([100,100,100,0]);
        }
        void Fresh(){p.Inventory.Clear();p.Stamina=100000;p.Gold=1000000;p.HardBattleCounts.Clear();p.LegendBattleCounts.Clear();p.HardDungeonBattleCounts.Clear();}
        GsNormalBattleSkipRequest Sweep(BscTable.GAME_DIFFICULTY diff,uint stage,uint count=1,bool dungeon=false)=>new(){stageId=stage,difficulty=diff,skipCount=count,dungeonFlag=dungeon};
        const GameServerCommand Skip=GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,End=GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST;
        foreach(var (diff,dungeon,id) in new[]{(Normal,false,13u),(Hard,false,13u),(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND,false,14u),(Hard,true,1u)}){
            var row=catalog.BattleStages(diff,dungeon)[id];Prepare(row);Fresh();
            var records=dungeon?p.HardDungeonStageRecords:diff==Normal?p.StageRecords:diff==Hard?p.HardStageRecords:p.LegendStageRecords;
            string label=dungeon?"Hidden dungeon":diff.ToString();
            records[id]=new(){Stars=3};
            int count=row.DailyLimit==1?1:2;
            var req=Sweep(diff,id,(uint)count,dungeon);var reply=await peer.Request<GsNormalBattleSkipOk>(Skip,req);var wire=peer.LastPayload.ToArray();
            Check(reply.earnedList.Count==count&&reply.earnedList.All(x=>x.earnedItemList.Single(i=>i.itemID==Material).itemCount==2&&x.earnedItemList.Single(i=>i.itemID==GroupMaterial).itemCount==2&&x.earnedItemList.Single(i=>i.itemID==Gem).itemCount==1),label+" boosts 1% material, group and gem rolls to certainty at their original quantities");
            Check(p.Inventory[Material]==2*count&&p.Inventory[GroupMaterial]==2*count&&p.Inventory[Gem]==count&&!p.Inventory.ContainsKey(NoDrop)&&p.Inventory[Piece]==count&&p.Inventory[Exp]==count*(4+row.CardRewardExp)&&p.Gold==1000000+count*row.Gold&&p.Stamina==100000-count*row.ConsumeStamina,label+" persists the exact stacks, preserves failed rolls, fragments, growth, gold and stamina");
            var state=State();await peer.Request<GsNormalBattleSkipOk>(Skip,req,retry:true);
            Check(State()==state&&wire.SequenceEqual(peer.LastPayload),label+" sweep retry cannot duplicate rewards or charges");
        }
        Fresh();p.StageRecords.Remove(13);p.ClearStage=12;
        var started=await peer.Request<GsNormalBattleStartOk>(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{stageid=13});
        var end=new GsNormalBattleEndConfirmRequest{stageId=13,timeStamp=started.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,battleEndSyncData=new(){clearTime=30}};
        var won=await peer.Request<GsNormalBattleEndConfirmOk>(End,end);var winBytes=peer.LastPayload.ToArray();
        Check(p.Inventory[Material]==5&&p.Inventory[GroupMaterial]==4&&p.Inventory[NoDrop]==3&&p.Inventory[Gem]==1&&won.earnedItemList.Single(x=>x.itemID==Material).itemCount==5,"Manual victory uses the boosted ordinary rolls and the original first-clear chances, top-ups and group draw count");
        string after=State();await peer.Request<GsNormalBattleEndConfirmOk>(End,end,retry:true);Check(State()==after&&winBytes.SequenceEqual(peer.LastPayload),"Manual victory retry returns the identical receipt");
        var saved=new PlayerStore(save,catalog).Find(p.Uid)!;Check(saved.Inventory[Material]==5&&saved.Inventory[GroupMaterial]==4&&saved.Inventory[Gem]==1,"Boosted rewards survive save reload");
        p.StageRecords[13]=new(){Stars=3};Fresh();p.Inventory[Material]=int.MaxValue-1;
        after=State();await peer.Request<FailPacket>(Skip,Sweep(Normal,13),success:false);Check(State()==after,"Overflow rejects the whole sweep without spending stamina or granting partial rewards");
        Fresh();normal.Value=1;
        Check(!await Advertises(150)&&rules.AdventureDropChanceMultiplier(Normal,false,ProtocolRouter.Now)==1,"Value 1 disables the operation for both display and settlement");
        var inactive=await peer.Request<GsNormalBattleSkipOk>(Skip,Sweep(Normal,13,10));
        Check(inactive.earnedList.Any(x=>x.earnedItemList.All(i=>i.itemID!=Gem))&&inactive.earnedList.All(x=>x.earnedItemList.Where(i=>i.itemID==Gem).Sum(i=>i.itemCount)<=1),"Disabled operation allows a normal 1% gem roll to miss on each sweep");
        Fresh();normal.Value=150;normal.Start="2020-01-01 00:00:00";normal.End="2020-01-01 00:00:01";
        var start=(ulong)new DateTimeOffset(2020,1,1,0,0,0,TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        Check(rules.AdventureDropChanceMultiplier(Normal,false,start-1)==1&&rules.AdventureDropChanceMultiplier(Normal,false,start)==150&&rules.AdventureDropChanceMultiplier(Normal,false,start+1)==150&&rules.AdventureDropChanceMultiplier(Normal,false,start+2)==1,"UTC+8 activity bounds are inclusive and expired events do not boost");
        var expired=await peer.Request<GsNormalBattleSkipOk>(Skip,Sweep(Normal,13,10));
        Check(expired.earnedList.Any(x=>x.earnedItemList.All(i=>i.itemID!=Gem)),"Expired event also rolls the base chance");
        Fresh();normal.Start="2000-01-01 00:00:00";normal.End="2099-12-31 23:59:59";
        catalog.Stages[13].DropProb.Clear();
        int optionalGemCount=0;
        for(int i=0;i<6;i++) {
            var fallback=await peer.Request<GsNormalBattleSkipOk>(Skip,Sweep(Normal,13,10));
            Check(fallback.earnedList.All(x=>x.earnedItemList.Single(i=>i.itemID==Material).itemCount==2&&x.earnedItemList.Where(i=>i.itemID==Gem).Sum(i=>i.itemCount)<=1),"Missing catalog probabilities use boosted fallback rolls with unscaled stacks, batch "+i);
            optionalGemCount+=fallback.earnedList.Count(x=>x.earnedItemList.Any(i=>i.itemID==Gem));
        }
        Check(optionalGemCount>0&&optionalGemCount<60&&p.Inventory[Gem]==optionalGemCount,"Optional gem drops and misses occur in real sweep results and match persisted quantities");
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var core=typeof(ProtocolRouter).Assembly;
        string suffix=core.GetName().Name=="Bnsm.EmbeddedServer"?"portable":"desktop";
        File.WriteAllText(Path.Combine(root,"..","Verification",$"adventure-drop-{suffix}-latest.json"),JsonSerializer.Serialize(new{passed=true,dropRule="probability",checks,serverCoreSha256=Hash(core.Location),attendanceConfigSha256=Hash(Path.Combine(root,"LocalServer/data/attendance.json")),productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"ADVENTURE_DROP_PASSED {suffix} {checks.Count}");
    }
}
