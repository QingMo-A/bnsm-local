using System.Net.Sockets;
using System.Text.Json;

internal static class LootTest
{
    private sealed class Peer(string root):IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();
        public async Task Login(LocalRuntime server) {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="loot-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload);
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request) {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(15000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task<byte[]> Request(GameServerCommand cmd,object request,bool success=true,bool advance=true) {
            if(advance)Key.sequenceNo++;var r=await Send(socket,(ushort)cmd,request);
            string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Command).ToString()!=expected)throw new Exception($"Expected {expected}, got {(GameServerCommand)r.Command}: "+Read<FailPacket>(r.Payload).message);
            return r.Payload;
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    public static void WriteFixture(string root,string output)
    {
        AdventureTest.WriteFixture(root,output);
        var file=Path.Combine(output,"players.json");var players=JsonSerializer.Deserialize<List<LocalPlayer>>(File.ReadAllText(file))!;var p=players[0];
        p.ClearStage=29;p.ClearHardStage=29;p.Stamina=3000;p.Inventory.Clear();p.Inventory[205080063]=2;p.Inventory[205080065]=1;p.Inventory[205070451]=1;
        p.StageRecords.Remove(13);p.StageRecords.Remove(14);p.StageRecords[15]=new(){Stars=3};
        File.WriteAllText(file,JsonSerializer.Serialize(players,new JsonSerializerOptions{WriteIndented=true}));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/loot",Guid.NewGuid().ToString("N"));
        await using var server=new LocalRuntime(root,save,0,0,0);var c=server.Catalog;var attendance=new AttendanceRules(root);
        using var peer=new Peer(root);await peer.Login(server);var p=server.Store.Find(peer.Key.uid)!;
        p.Level=99;p.Exp=(uint)c.PlayerStates.First(x=>x.Level==99).Exp;p.Gold=1000000;p.Stamina=100000;p.ClearStage=179;p.ClearHardStage=179;p.ClearLegendStage=179;
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        string State()=>JsonSerializer.Serialize(new {p.Gold,p.CashPoint,p.Exp,p.Level,p.Stamina,p.Moonstone,p.Inventory,p.EquipmentItems,p.CardIds,p.CardStates,p.NextEquipmentSequence,p.ActiveStage,p.StageRecords,p.HardStageRecords,p.LegendStageRecords,p.HardBattleCounts,p.LootReceipts});
        async Task Bad(GameServerCommand cmd,object req,string label,bool advance=true){string before=State();await peer.Request(cmd,req,false,advance);Check(State()==before,label+" leaves all rewards and resources unchanged");}
        GsOpenInventoryBoxItemRequest Box(uint id=205080063,uint count=1)=>new(){keyInfo=peer.Key,boxItemId=id,openCount=count};
        var gems=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Step==3).Select(x=>(uint)x.Id).ToHashSet();
        p.Inventory[205080063]=10;var request=Box(count:10);byte[] bytes=await peer.Request(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,request);var opened=Read<GsOpenInventoryBoxItemOk>(bytes);
        Check(!p.Inventory.ContainsKey(205080063)&&opened.deltaItemInfoList.Single(x=>x.itemID==205080063).itemCount==-10,"Screenshot five-sided gem box consumes exactly the requested ten boxes");
        Check(opened.deltaItemInfoList.Where(x=>x.itemCount>0).Sum(x=>x.itemCount)==10&&opened.deltaItemInfoList.Where(x=>x.itemCount>0).All(x=>gems.Contains(x.itemID)&&x.itemSeq==0),"Ten gem boxes return exactly ten gems from the eight original weighted outcomes");
        string after=State();byte[] replay=await peer.Request(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,request,true,false);Check(State()==after&&replay.SequenceEqual(bytes),"Random box retry returns identical reward bytes without rerolling or consuming twice");
        request.openCount=1;await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,request,"Changed successful box sequence",false);
        await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(count:0),"Zero-count box request");
        await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(count:11),"Box batch above original ten-box limit");
        await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(),"Unowned box");
        await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(120101003),"A gem submitted as a box");
        p.Inventory[205080063]=1;p.ActiveStage=13;await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(),"Open box during active battle");p.ActiveStage=null;
        foreach(uint id in gems)p.Inventory[id]=int.MaxValue;await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(),"Any random gem reward would overflow");
        p.Inventory.Clear();p.Inventory[205080065]=1;
        var equipment=Read<GsOpenInventoryBoxItemOk>(await peer.Request(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(205080065)));
        Check(equipment.deltaItemInfoList.Where(x=>x.itemCount>0).Select(x=>x.itemID).Order().SequenceEqual(new uint[]{109073079,109073080}),"Fixed equipment pack gives both original advertised items");
        Check(equipment.deltaItemInfoList.Where(x=>x.itemCount>0).All(x=>x.itemSeq>uint.MaxValue)&&p.EquipmentItems.Select(x=>x.Sequence).Distinct().Count()==2,"Box equipment gets independent persistent 64-bit instance serials");
        p.Inventory[205070451]=2;var moon=Read<GsOpenInventoryBoxItemOk>(await peer.Request(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(205070451,2)));
        Check(moon.addMoonStone==20&&moon.currentMoonStone==20&&p.Moonstone==20,"Moonstone box updates delta, current balance and persistent currency");
        var relic=Read<GsGetClassRelicInfoOk>(await peer.Request(GameServerCommand.GS_GET_CLASS_RELIC_INFO_REQUEST,new GsGetClassRelicInfoRequest{keyInfo=peer.Key}));Check(relic.relicPoint==20,"New-login relic query returns saved box currency");
        p.Inventory[205070673]=1;p.Gold=uint.MaxValue;await Bad(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box(205070673),"Currency overflow box");p.Gold=1000000;
        // All supplied box reward IDs are available in this recovered version.
        int boxes=0;foreach(var box in c.Boxes) {
            p.Inventory.Clear();p.EquipmentItems.Clear();p.Gold=p.CashPoint=p.Moonstone=0;p.Inventory[(uint)box.Id]=1;
            await peer.Request(GameServerCommand.GS_OPEN_INVENTORY_BOX_ITEM_REQUEST,Box((uint)box.Id));
            Check(p.Inventory.GetValueOrDefault((uint)box.Id)==0,$"Original box {box.Id} opens atomically with valid original rewards");boxes++;
        }
        p.Gold=1000000;p.Stamina=100000;p.Exp=(uint)c.PlayerStates.First(x=>x.Level==99).Exp;p.Level=99;
        GsNormalBattleSkipRequest Sweep(uint stage=13,uint count=1,BscTable.GAME_DIFFICULTY diff=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,bool dungeon=false)=>new(){keyInfo=peer.Key,stageId=stage,skipCount=count,difficulty=diff,dungeonFlag=dungeon};
        await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(),"Sweep without three earned stars");p.StageRecords[13]=new(){Stars=3};
        await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(count:0),"Zero sweep count");
        await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(count:11),"Sweep exceeds original normal count ten");
        var cash=Sweep();cash.useCash=true;await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,cash,"Inactive cash sweep mode");
        p.Stamina=9;await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(),"Insufficient sweep stamina");p.Stamina=100000;
        p.Inventory.Clear();p.EquipmentItems.Clear();uint gold=p.Gold,stamina=p.Stamina;var sweep=Sweep(count:10);
        bytes=await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,sweep);var swept=Read<GsNormalBattleSkipOk>(bytes);
        Check(swept.earnedList.Count==10&&swept.earnedList.Select(x=>x.skipSequence).SequenceEqual(Enumerable.Range(1,10).Select(x=>(uint)x)),"Ten-sweep reply preserves all ten original result pages");
        var rolledEquipment=swept.earnedList.SelectMany(x=>x.earnedItemList)
            .Where(i=>c.ItemsById[(int)i.itemID].ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).ToArray();
        Check(p.Stamina==stamina-100&&p.Gold==gold+5000&&p.EquipmentItems.Count==rolledEquipment.Length&&
            rolledEquipment.Length<=10&&rolledEquipment.All(x=>x.itemCount==1&&p.EquipmentItems.Any(y=>y.Sequence==x.itemSeq)),
            "Normal sweep consumes exact stamina, grants gold and persists only the equipment that rolled");
        Check(p.Inventory[210002001]==240&&swept.deltaExpItemInfo.itemCount==140&&swept.earnedList.Sum(x=>x.earnedItemList.Where(i=>i.itemID==210002001).Sum(i=>i.itemCount))==100,"NPC preview experience items and shared card growth are each credited exactly once");
        after=State();replay=await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,sweep,true,false);Check(State()==after&&replay.SequenceEqual(bytes),"Sweep retry neither charges stamina twice nor adds extra equipment");
        p.HardStageRecords[13]=new(){Stars=3};p.HardBattleCounts[13]=0;p.Stamina=100000;
        await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(13,3,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD));
        Check(p.HardBattleCounts[13]==3,"Hard-mode sweep counts all three entries against the same daily battle counter");
        await Bad(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(13,1,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD),"Exhausted hard-stage entries");
        p.LegendStageRecords[14]=new(){Stars=3};p.LegendBattleCounts[14]=0;await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(14,1,BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND));
        Check(p.LegendBattleCounts[14]==1,"Legend sweep uses its independent daily count");
        p.HardDungeonStageRecords[1]=new(){Stars=3};p.HardDungeonBattleCounts[1]=0;await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_SKIP_REQUEST,Sweep(1,1,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,true));
        Check(p.HardDungeonBattleCounts[1]==1,"Hidden dungeon sweep uses the dungeon record and daily count");
        int stages=0;
        foreach(var diff in new[]{BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND})foreach(bool dungeon in new[]{false,true})foreach(var stage in c.BattleStages(diff,dungeon).Values) {
            if(!dungeon&&!StageUnlock.CanEnter(c,p,diff,(uint)stage.Id))continue;
            if(dungeon&&!StageUnlock.FieldOpen(c,p,diff,stage.FieldId))continue;
            p.Inventory.Clear();p.EquipmentItems.Clear();p.Stamina=100000;p.Gold=1000000;p.HardBattleCounts.Clear();p.LegendBattleCounts.Clear();p.HardDungeonBattleCounts.Clear();
            var started=Read<GsNormalBattleStartOk>(await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{keyInfo=peer.Key,stageid=(uint)stage.Id,difficulty=diff,dungeonFlag=dungeon?1u:0u}));
            var ending=new GsNormalBattleEndConfirmRequest{keyInfo=peer.Key,stageId=(uint)stage.Id,difficulty=diff,dungeonFlag=dungeon?1u:0u,timeStamp=started.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_SUCCESS,battleEndSyncData=new(){clearTime=30}};
            var reward=Read<GsNormalBattleEndConfirmOk>(await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,ending));
            bool adventure=dungeon||stage.Id>GameCatalog.TutorialEndStage;
            int multiplier=adventure?attendance.AdventureDropChanceMultiplier(diff,dungeon,ProtocolRouter.Now):1;
            Check(reward.earnedCoinInfo.gold==stage.Gold&&stage.NpcRewardId.Select((id,i)=>{
                var type=stage.NpcRewardIdType[i];
                if(type!=BscTable.NPC_REWARD_TYPE.NPC_REWARD_TYPE_ITEM)return true;
                double chance=stage.DropProb.Count!=0?stage.DropProb[i]:adventure?LocalAdventureDropRules.BaseChance(type,c.ItemsById[id].ItemType):100;
                return LocalAdventureDropRules.EffectiveChance(chance,multiplier)<100||reward.earnedItemList.Where(x=>x.itemID==id).Sum(x=>x.itemCount)>=stage.RewardQuantity[i];
            }).All(x=>x),$"Victory {diff}, dungeon={dungeon}, stage={stage.Id} delivers guaranteed items and gold");stages++;
        }
        p.Inventory.Clear();p.EquipmentItems.Clear();p.HardBattleCounts.Clear();
        var startLoss=Read<GsNormalBattleStartOk>(await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_START_REQUEST,new GsNormalBattleStartRequest{keyInfo=peer.Key,stageid=13,difficulty=BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL}));
        gold=p.Gold;var lost=new GsNormalBattleEndConfirmRequest{keyInfo=peer.Key,stageId=13,timeStamp=startLoss.timeStamp,endType=NormalBattleEndType.NORMAL_BATTLE_FAIL};
        var loss=Read<GsNormalBattleEndConfirmOk>(await peer.Request(GameServerCommand.GS_NORMAL_BATTLE_END_CONFIRM_REQUEST,lost));
        Check(loss.earnedItemList.Count==0&&loss.earnedCardList.Count==0&&p.Gold==gold&&p.EquipmentItems.Count==0,"Losing or abandoning a battle never grants victory loot");
        server.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.LootReceipts.Count>0&&restored.Moonstone==p.Moonstone&&restored.Inventory.Count==p.Inventory.Count,"Box currency, inventory and reward replay receipts survive save reload");
        File.WriteAllText(Path.Combine(root,"evidence/loot-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,boxes,stages,checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Loot protocol: {checks.Count}/{checks.Count}; original boxes={boxes}, stage victories={stages}");
    }
}
