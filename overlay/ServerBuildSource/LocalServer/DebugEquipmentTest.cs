using System.Net.Http.Json;
using System.Text.Json;

internal static class DebugEquipmentTest
{
    internal static void Prepare(LocalRuntime r)
    {
        DebugRulesTest.Prepare(r);var p=r.Store.GetOrCreate("local-player");var c=r.Catalog;
        p.CardIds[0]=20103;p.CardStates[1]=new(){Level=99,Exp=ProgressionRules.CardCeiling(c,99)};
        var hero=c.Cards.First(x=>x.Id==20103);
        var refine=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.Step==1&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL&&x.CardId==0&&x.Next1Id>0);
        var awake=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL&&x.CardId==0&&x.AwakningNextId>0);
        p.EquipmentItems.Clear();p.NextEquipmentSequence=1UL<<40;
        foreach(var row in new[]{refine,awake})p.EquipmentItems.Add(new(){ItemId=(uint)row.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        var engraved=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.CardId==0&&x.Empower&&x.EmpowerMaxCost>5&&x.Rank>=c.Numbers[BscTable.NUMBER_TYPE.EMPOWERSTONE_LIMIT_RANK]);
        p.EquipmentItems.Add(new(){ItemId=(uint)engraved.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        p.CardStates[1].WeaponSequence=p.EquipmentItems[0].Sequence;
        p.CardStates[1].GemIds[0]=120101003;p.Inventory[120101003]=3;p.Inventory[260001001]=2;
        p.Raids.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now);for(int i=1;i<=4;i++){p.Raids.Used[i]=100;p.Raids.Bought[i]=1;p.Raids.Latest[i]=ProtocolRouter.Now;}
        p.Raids.BossHp[1]=123;p.Raids.BestDamage[1]=456;
        p.ChallengeTower.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now);p.ChallengeTower.Week=(p.ChallengeTower.Day+3)/7;p.ChallengeTower.UsedEntries=99;
        p.Wander.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now);p.Wander.Resets=99;p.Wander.PaidLeft=2;
        p.ClearHardStage=179;p.Wander.Difficulty=2;p.Wander.Spot=126;p.Wander.Complete=true;p.Wander.Points=23940;p.Wander.BestPoints=23940;
        p.ExchangeDay=(ProtocolRouter.Now+8*3600)/86400;p.HardBattleCounts[7]=3;p.LegendBattleCounts[7]=3;p.HardDungeonBattleCounts[1]=3;r.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        string full=Path.GetFullPath(output),scope=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;
        if(!full.StartsWith(scope,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(full);await using var r=new LocalRuntime(root,Path.Combine(full,"saves"),0,0,0,false);
        if(r.Store.All().Count==0)Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(full,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/debug-equipment",Guid.NewGuid().ToString("N"));
        await using var r=new LocalRuntime(root,save,0,0,0,false);Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        using var http=new HttpClient{BaseAddress=new Uri(addresses.Addresses.Single())};var p=r.Store.GetOrCreate("local-player");var c=r.Catalog;var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        async Task<JsonElement> Post(string url,object body){using var response=await http.PostAsJsonAsync(url,body);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();}
        async Task Toggle(int index,bool on){var result=await Post("/debug/options",new{uid=p.Uid,option=index,enabled=on});Check(result.GetProperty("ok").GetBoolean(),$"Option {index}={on} accepted");}
        for(int i=10;i<LocalDebugFlags.OptionCount;i+=2) {
            await Toggle(i+1,true);Check((p.DebugFlags&(3u<<i))==(3u<<i),"Child enables free parent "+i);
            await Toggle(i+1,false);Check((p.DebugFlags&(3u<<i))==(1u<<i),"Child off retains parent "+i);
            await Toggle(i,false);Check((p.DebugFlags&(3u<<i))==0,"Parent off disables both "+i);
        }
        Check(!(await Post("/debug/options",new{option=20,enabled=true})).GetProperty("ok").GetBoolean(),"Unknown flag rejected");
        var connection=new ConnectionState{Player=p};r.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,WireCodec.Serialize(new LsLoginRequest{userID="local-player",userPW="local"}),connection);
        var login=(LsGameStartOk)r.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,WireCodec.Serialize(new LsGameStartRequest{serverId=1,bAgreement=true}),connection).Message!;
        var key=new SessionKeyInfo{uid=p.Uid,loginSessionKey=login.loginSessionKey};
        Reply Send(GameServerCommand cmd,object body,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;var result=r.Router.Handle(ServiceKind.Game,(ushort)cmd,WireCodec.Serialize(body),connection);
            Check(result.Name.EndsWith(success?"_OK":"_FAIL"),cmd+" -> "+result.Name+" "+(result.Message as FailPacket)?.message);return result;
        }
        BscTable.ItemData Item(uint id)=>c.ItemsById[(int)id];
        string Wallet()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Inventory});
        string State()=>JsonSerializer.Serialize(new{p.EquipmentItems,p.Inventory,p.Gold,p.CardStates,p.CraftingReceipts,p.EquipmentReceipts});
        var gear=p.EquipmentItems[0];ulong seq=gear.Sequence;uint original=gear.ItemId;
        GsEquipmentCraftRequest Refine()=>new(){keyInfo=key,cardSeq=1,baseEquipmentSeq=seq,targetEquipmentId=(uint)Item(gear.ItemId).Next1Id};
        string before=State();Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(),false);Check(State()==before,"Normal refinement still needs materials");
        await Toggle(10,true);string wallet=Wallet();uint target=(uint)Item(gear.ItemId).Next1Id;
        var request=Refine();Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,request);Check(gear.ItemId==target&&Wallet()==wallet,"Free refine follows one link without costs");
        before=State();Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,request,retry:true);Check(State()==before,"Refine retry does not advance twice");
        await Toggle(11,true);Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine());var final=Item(gear.ItemId);
        Check(final.Id!=original&&final.Next1Id==0&&final.EquipmentType==Item(original).EquipmentType&&Wallet()==wallet,"Max refine stops at original refinement end");
        Check(p.CardStates[1].WeaponSequence==seq&&gear.Sequence==seq,"Unique serial and wearer preserved");
        Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,new GsEquipmentCraftRequest{keyInfo=key,baseEquipmentSeq=seq,targetEquipmentId=original},false);
        await Toggle(10,false);
        var awakened=p.EquipmentItems[1];uint awakeBase=awakened.ItemId;
        GsEquipmentAwakeRequest Awake()=>new(){keyInfo=key,baseEquipmentSeq=awakened.Sequence,targetEquipmentId=(uint)Item(awakened.ItemId).AwakningNextId};
        await Toggle(12,true);Send(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake());Check(awakened.ItemId==Item(awakeBase).AwakningNextId&&Wallet()==wallet,"Free equipment awakening uses original link");await Toggle(12,false);
        // Another untouched instance verifies max awakening separately.
        var duplicate=new LocalOwnedEquipment{ItemId=awakeBase,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(duplicate);
        await Toggle(13,true);Send(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,new GsEquipmentAwakeRequest{keyInfo=key,baseEquipmentSeq=duplicate.Sequence,targetEquipmentId=(uint)Item(awakeBase).AwakningNextId});Check(duplicate.ItemId!=awakeBase&&Wallet()==wallet,"Max awakening follows awakening links without charging");await Toggle(12,false);
        var gem=new GsGemCraftRequest{keyInfo=key,baseGemId=120101003,targetGemId=120101004,slotIndex=2,craftCount=1};
        await Toggle(14,true);Send(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem);Check(p.Inventory[120101003]==2&&p.Inventory[120101004]==1&&p.Gold==0,"Free bag gem transforms one owned base only");
        await Toggle(15,true);gem.cardSeq=1;gem.slotIndex=0;gem.craftCount=0;wallet=Wallet();Send(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem);
        uint gemEnd=p.CardStates[1].GemIds[0];Check(Item(gemEnd).Step==c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_GEM)&&Wallet()==wallet,"Max worn gem reaches original cap without consuming bag stacks");
        gem.cardSeq=0;gem.slotIndex=2;gem.craftCount=999;before=State();Send(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem,false);Check(State()==before,"Debug cannot synthesize unowned bases or oversized batch");await Toggle(14,false);
        var stone=new GsEmpowerStoneCraftRequest{KeyInfo=key,BaseItemId=260001001,TargetItemId=260002001};
        await Toggle(18,true);Send(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,stone);Check(p.Inventory[260001001]==1&&p.Inventory[260002001]==1,"Free stone synthesis transforms one base");
        await Toggle(19,true);Send(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,stone);Check(!p.Inventory.ContainsKey(260001001)&&p.Inventory.Any(x=>Item(x.Key).ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&(Item(x.Key).Next1Id==0||Item(x.Key).Rank>=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_EMPOWER))),"Max stone synthesis reaches version cap: "+string.Join(",",p.Inventory.Where(x=>Item(x.Key).ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE).Select(x=>$"{x.Key}/rank={Item(x.Key).Rank}/next={Item(x.Key).Next1Id}"))+" limit="+c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_EMPOWER));await Toggle(18,false);
        var engraved=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Empower&&x.EmpowerMaxCost>5&&x.Rank>=c.Numbers[BscTable.NUMBER_TYPE.EMPOWERSTONE_LIMIT_RANK]);
        var e=new LocalOwnedEquipment{ItemId=(uint)engraved.Id,Sequence=p.NextEquipmentSequence++};p.EquipmentItems.Add(e);
        var empower=new GsEmpowerEquipmentItemRequest{keyInfo=key,equipmentItemSeq=e.Sequence};wallet=Wallet();
        await Toggle(16,true);Send(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,empower);Check(e.CurrentEmpowerValue>0&&e.CurrentEmpowerValue<engraved.EmpowerMaxCost&&Wallet()==wallet,"Free engraving works without stones or gold");
        await Toggle(17,true);Send(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,empower);Check(e.CurrentEmpowerValue==engraved.EmpowerMaxCost&&e.EmpowerValues.Any(x=>x>0)&&Wallet()==wallet,"Max engraving fills original capacity with real stone attributes");
        before=State();Send(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,empower,false);Check(State()==before,"Full engraving cannot overflow");await Toggle(16,false);
        await Toggle(10,true);p.ActiveStage=1;before=State();Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(),false);Check(State()==before,"Battle blocks debug crafting");
        var blocked=await Post("/debug/challenges/restore",new{mode="all"});Check(!blocked.GetProperty("ok").GetBoolean()&&p.Raids.Used.Count==4,"Battle blocks count restore atomically");p.ActiveStage=null;
        p.TrainingSlots[0]=new(){CardSequence=1};gear.ItemId=original;before=State();Send(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(),false);Check(State()==before,"Training wearer blocks debug crafting");p.TrainingSlots.Clear();await Toggle(10,false);
        string Records()=>JsonSerializer.Serialize(new{p.Raids.BossHp,p.Raids.BestDamage,p.Raids.Bought,p.Wander.PaidLeft,p.Wander.Claimed,p.Wander.Spot,p.ChallengeTower.Areas,p.ChallengeTower.DeadCards,p.ChallengeTower.BestFloor,p.StageRecords,p.HardStageRecords,p.HardBattleResetCounts});
        p.Wander.Claimed.Add(1);p.Wander.Spot=5;p.ChallengeTower.Areas[1]=3;p.ChallengeTower.BestFloor=2;p.ChallengeTower.DeadCards.Add(2);p.HardBattleResetCounts[7]=1;
        string records=Records();var restored=await Post("/debug/challenges/restore",new{mode="world-boss"});Check(restored.GetProperty("ok").GetBoolean()&&!p.Raids.Used.ContainsKey(2)&&p.Raids.Used.ContainsKey(1)&&p.Raids.Used.ContainsKey(3)&&!p.Raids.Latest.ContainsKey(2),"Individual blood-battle restoration is isolated and clears cooldown");
        restored=await Post("/debug/challenges/restore",new{mode="hard"});Check(restored.GetProperty("ok").GetBoolean()&&p.HardBattleCounts.Count==0&&p.LegendBattleCounts.Count>0,"Hero attempt restore is separate from legend attempts");
        var hard=(GsGetHardModeBattleCountOk)Send(GameServerCommand.GS_GET_HARDMODE_BATTLE_COUNT_REQUEST,new GsGetHardModeBattleCountRequest{keyInfo=key}).Message!;
        Check(hard.stageBattleCount.Count==0&&hard.stageLegendBattleCount.Count>0,"Native count protocol exposes restored hero entries immediately");
        restored=await Post("/debug/challenges/restore",new{mode="legend"});
        var legend=(GsGetHardModeBattleCountOk)Send(GameServerCommand.GS_GET_HARDMODE_BATTLE_COUNT_REQUEST,new GsGetHardModeBattleCountRequest{keyInfo=key}).Message!;
        Check(restored.GetProperty("ok").GetBoolean()&&legend.stageLegendBattleCount.Count==0,"Native count protocol exposes restored legend entries immediately");
        restored=await Post("/debug/challenges/restore",new{mode="all"});Check(restored.GetProperty("ok").GetBoolean()&&p.Raids.Used.Count==0&&p.Raids.Latest.Count==0&&p.ChallengeTower.UsedEntries==0&&p.Wander.Resets==0&&p.HardBattleCounts.Count==0&&p.LegendBattleCounts.Count==0&&p.HardDungeonBattleCounts.Count==0,"All supported daily battle counters restored");
        Check(Records()==records,"Restoring counts preserves boss HP, paid entries, claims, deaths, progress and records");
        var wander=(GsGetWanderBattleRecordOk)Send(GameServerCommand.GS_GET_WANDER_BATTLE_RECORD_REQUEST,new GsGetWanderBattleRecordRequest{keyInfo=key}).Message!;
        Check(wander.wanderBattleRecord.freeResetCount==0&&wander.wanderBattleRecord.todayResetCount==0,"Native wander record exposes restored reset opportunity");
        string saved=File.ReadAllText(Path.Combine(save,"players.json"));await Post("/debug/challenges/restore",new{mode="all"});Check(File.ReadAllText(Path.Combine(save,"players.json"))==saved,"Repeated restore is idempotent");
        Check(!(await Post("/debug/challenges/restore",new{mode="evil-war"})).GetProperty("ok").GetBoolean(),"Infinite challenge rebirth count is not a daily attempt counter");
        var reloaded=new PlayerStore(save,c).Find(p.Uid)!;Check(reloaded.EquipmentItems.Single(x=>x.Sequence==e.Sequence).CurrentEmpowerValue==e.CurrentEmpowerValue&&reloaded.CardStates[1].GemIds[0]==gemEnd&&reloaded.Raids.Used.Count==0,"Growth and restored counts survive server reload");
        File.WriteAllText(Path.Combine(root,"evidence/debug-equipment-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("DEBUG_EQUIPMENT_PASS "+checks.Count);
    }
}
