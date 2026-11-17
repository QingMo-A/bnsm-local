using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

internal static class CraftingTest
{
    public static void WriteFixture(string root,string output)
    {
        if(!Path.GetFullPath(output).StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Dedicated verification folder required");
        var c=new GameCatalog(root);var hero=c.Cards.First(x=>x.Id==20103);
        var p=new LocalPlayer {Uid=100001,Account="local-player",Level=99,Exp=(uint)c.PlayerStates.First(x=>x.Level==99).Exp,Gold=100000000,CashPoint=100000,CardIds=c.StarterCards(),Deck=[1,2,3,4,5],SkillPoint=10,SkillPointStamp=ProtocolRouter.Now};
        p.CardIds[0]=20103;p.CardStates[1]=new() {Level=99};
        var basic=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.Step==5&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL&&x.CardId==0);
        var second=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.Step==12&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME&&x.AwakningNextId!=0&&x.CardId==0);
        foreach(var item in new[]{basic,basic,second})p.EquipmentItems.Add(new() {ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        p.CardStates[1].WeaponSequence=p.EquipmentItems[0].Sequence;
        foreach(var item in c.Items.Where(x=>x.ItemType is BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT or BscTable.ITEM_TYPE.ITEM_TYPE_GEM))p.Inventory[(uint)item.Id]=1000;
        p.CardStates[1].GemIds[0]=120101003;p.Inventory[120101003]=7;
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"players.json"),JsonSerializer.Serialize(new[]{p},new JsonSerializerOptions {WriteIndented=true}));
    }
    public static async Task Run(string root)
    {
        var protectedFiles=new[]{Path.Combine(root,"LocalServer/saves/players.json"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BnsmLocalGame")};
        var hashes=protectedFiles.SelectMany(x=>File.Exists(x)?new[]{x}:Directory.Exists(x)?Directory.GetFiles(x,"players.json",System.IO.SearchOption.AllDirectories):[]).ToDictionary(x=>x,x=>SHA256.HashData(File.ReadAllBytes(x)));
        string save=Path.Combine(root,"verification/crafting",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool value,string text) {if(!value)throw new Exception(text);checks.Add(text);}
        T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);var c=server.Catalog;
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        async Task<byte[]> Login(LoginServerCommand cmd,object req) {await login.GetStream().WriteAsync(codec.Encode((ushort)cmd,WireCodec.Serialize(req),true));using var timeout=new CancellationTokenSource(15000);return codec.Decode((await WireCodec.ReadFrame(login.GetStream(),timeout.Token))!,true).Payload;}
        await Login(LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="crafting-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var start=Read<LsGameStartOk>(await Login(LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true}));
        var key=new SessionKeyInfo {uid=start.uid,loginSessionKey=start.loginSessionKey};var p=server.Store.Find(start.uid)!;
        p.Level=99;p.Gold=uint.MaxValue;p.CardIds[0]=20103;p.CardStates[1]=new() {Level=99};
        using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);byte[] last=[];
        async Task<byte[]> Request(GameServerCommand cmd,object req,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;
            await game.GetStream().WriteAsync(codec.Encode((ushort)cmd,WireCodec.Serialize(req),true));using var timeout=new CancellationTokenSource(15000);
            var r=codec.Decode((await WireCodec.ReadFrame(game.GetStream(),timeout.Token))!,true);string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Command).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(r.Payload).message);last=r.Payload;return r.Payload;
        }
        string State()=>JsonSerializer.Serialize(new {p.Gold,p.Inventory,p.EquipmentItems,p.CardStates,p.CraftingReceipts});
        async Task Reject(GameServerCommand cmd,object req,string label,bool retry=false) {string before=State();await Request(cmd,req,false,retry);Check(State()==before,label+" rejects without changing coins, materials, equipment, sockets or receipts");}
        BscTable.ItemData Item(uint id)=>c.Items.First(x=>x.Id==id);
        LocalOwnedEquipment Grant(BscTable.ItemData item) {var e=new LocalOwnedEquipment {ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(e);return e;}
        void Materials(BscTable.ItemData item,bool awake=false) {var ids=awake?item.AwakningMaterialId:item.Material1Id;foreach(int id in ids)p.Inventory[(uint)id]=1000000;p.Gold=uint.MaxValue;}
        GsEquipmentCraftRequest Refine(LocalOwnedEquipment item,ulong owner=0,uint? target=null)=>new() {keyInfo=key,cardSeq=owner,baseEquipmentSeq=item.Sequence,targetEquipmentId=target??(uint)Item(item.ItemId).Next1Id};
        GsEquipmentAwakeRequest Awake(LocalOwnedEquipment item,ulong owner=0,uint? target=null)=>new() {keyInfo=key,cardSeq=owner,baseEquipmentSeq=item.Sequence,targetEquipmentId=target??(uint)Item(item.ItemId).AwakningNextId};
        var basic=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==c.Cards.First(x=>x.Id==20103).Class&&x.Step==5&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL&&x.CardId==0);
        var gear=Grant(basic);p.CardStates[1].WeaponSequence=gear.Sequence;Materials(basic);
        var req=Refine(gear,1);var refined=Read<GsEquipmentCraftOk>(await Request(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,req));byte[] receipt=last.ToArray();
        Check(gear.ItemId==basic.Next1Id&&refined.changedCardInfo.weaponSeq==gear.Sequence&&p.EquipmentItems.Count==1,"Worn refinement replaces the same unique equipment instance and preserves its slot");
        Check(refined.userItemList.Count(x=>x.itemSeq!=0)==1&&refined.userItemList.Single(x=>x.itemSeq!=0).itemID==gear.ItemId&&refined.userItemList.Where(x=>x.itemSeq==0).All(x=>x.itemCount<0),"APK ConvertItemList receives exactly one serial and stack deltas have serial zero");
        string after=State();await Request(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,req,true,true);Check(State()==after&&last.SequenceEqual(receipt),"Refinement packet retry replays exact bytes with no second debit");
        req.targetEquipmentId++;await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,req,"Changed successful sequence",true);
        var six=Item(gear.ItemId);Materials(six,true);var wake=Awake(gear,1);uint gold=p.Gold;
        var awake=Read<GsEquipmentAwakeOk>(await Request(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,wake));
        Check(gear.ItemId==six.AwakningNextId&&p.CardStates[1].WeaponSequence==gear.Sequence&&awake.changedCardInfo.cardSeq==1,"First awakening retains wearer, unique serial and card loadout");
        Check(gold-p.Gold==3380&&six.AwakningMaterialId.Select((id,i)=>p.Inventory[(uint)id]==1000000-six.AwakningMaterialValue[i]).All(x=>x),"First awakening consumes original 3380 gold and exact awakening materials");
        Materials(Item(gear.ItemId));await Request(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,1));Check(Item(gear.ItemId).EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME&&Item(gear.ItemId).Step==7,"Refinement continues along awakened equipment chain");
        await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,0),"Worn item submitted as bag equipment");
        await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,9999),"Wrong wearer");
        await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,1,(uint)basic.Id),"Backward or unrelated equipment target");
        p.CardStates[1].Level=1;await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,1),"Wearer below target equipment level");p.CardStates[1].Level=99;
        p.Level=7;await Reject(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(gear,1),"Original player-level craft gate");p.Level=99;
        var bag=Grant(six);Materials(six,true);p.Level=29;await Reject(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(bag),"First awakening level 30 gate");p.Level=99;
        p.Gold=0;await Reject(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(bag),"Awakening insufficient coins");p.Gold=uint.MaxValue;
        p.Inventory.Remove((uint)six.AwakningMaterialId[0]);await Reject(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(bag),"Awakening missing stone");Materials(six,true);
        var bagResult=Read<GsEquipmentAwakeOk>(await Request(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(bag)));Check(bagResult.changedCardInfo!=null&&bagResult.changedCardInfo.cardSeq==0&&bagResult.userItemList.Single(x=>x.itemSeq!=0).cardSeq==0,"Bag awakening returns the required empty card record and unowned wearer metadata");
        var secondSource=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_EXTREME&&x.AwakningNextId!=0);var second=Grant(secondSource);Materials(secondSource,true);
        p.Level=59;await Reject(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(second),"Second awakening level 60 gate");p.Level=99;
        await Request(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(second));Check(Item(second.ItemId).EquipmentType==BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_MORE_EXTREME,"Second awakening reaches original ultimate equipment ID");
        GsGemCraftRequest Gem(uint id=120101003,uint count=1,ulong card=0,uint slot=0)=>new() {keyInfo=key,baseGemId=id,targetGemId=(uint)Item(id).Next1Id,craftCount=count,cardSeq=card,slotIndex=slot};
        p.Inventory[120101003]=12;p.Inventory[120101004]=0;p.Gold=1000000;var gemRequest=Gem(count:3,slot:2);
        var gems=Read<GsGemCraftOk>(await Request(GameServerCommand.GS_GEM_CRAFT_REQUEST,gemRequest));
        Check(p.Inventory.GetValueOrDefault(120101003u)==0&&p.Inventory[120101004]==3&&p.Gold==999520,"Bag batch gem synthesis consumes four base gems per result and 160 gold each");
        Check(gems.changedCardInfo!=null&&gems.changedCardInfo.cardSeq==0&&gems.userItemList.Any(x=>x.itemID==120101003&&x.itemCount==-12)&&gems.userItemList.Any(x=>x.itemID==120101004&&x.itemCount==3),"Gem batch response contains correct signed inventory deltas");
        receipt=last.ToArray();after=State();await Request(GameServerCommand.GS_GEM_CRAFT_REQUEST,gemRequest,true,true);Check(State()==after&&last.SequenceEqual(receipt),"Gem batch retry cannot create extra gems");
        p.Inventory[120101003]=3;p.CardStates[1].GemIds=[120101003,120101003,0];p.Gold=1000000;
        var socket=Read<GsGemCraftOk>(await Request(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(count:0,card:1,slot:0)));
        Check(p.CardStates[1].GemIds.SequenceEqual(new uint[]{120101004,120101003,0})&&!p.Inventory.ContainsKey(120101003)&&p.Inventory[120101004]==3&&p.Gold==999840,"Socket upgrade consumes only three bag gems plus the selected worn gem, with no unwear fee or extra result");
        Check(socket.changedCardInfo.jewelID.SequenceEqual(p.CardStates[1].GemIds),"Socket synthesis reports the complete new three-gem loadout");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(count:2,card:1,slot:1),"Batch request on a single socket");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(card:1,slot:3),"Gem socket outside original wire indexes 0..2");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(card:1,slot:2),"Empty gem socket");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(count:0),"Zero bag batch");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(count:1000),"Gem batch exceeds native 999 limit");
        await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(),"Missing base gems");
        p.Inventory[120101003]=4;p.Inventory[120101004]=int.MaxValue;await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(),"Gem output overflow");p.Inventory[120101004]=3;
        var wrong=Gem();wrong.targetGemId=120201004;await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,wrong,"Unrelated gem target");
        p.ActiveStage=c.FirstAdventureStage;await Reject(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem(),"Craft during active battle");p.ActiveStage=null;
        // A refinement needing alloy 260000003 can make it automatically from
        // raw copper/iron; missing ingredients must leave even earlier debits intact.
        var recursiveSource=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Next1Id!=0&&x.Material1Id.Contains(260000003));var recursive=Grant(recursiveSource);
        p.Inventory.Clear();foreach(int id in recursiveSource.Material1Id)p.Inventory[(uint)id]=100;
        p.Inventory.Remove(260000003);p.Inventory[260000001]=100;p.Inventory[260000002]=100;p.Gold=uint.MaxValue;
        await Request(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(recursive));Check(p.Inventory[260000001]<100&&p.Inventory[260000002]<100&&!p.Inventory.ContainsKey(260000003),"Refinement recursively crafts missing alloys from original recipes in one atomic operation");
        p.Inventory.Clear();p.Inventory[260000001]=3;p.Inventory[260000002]=3;p.Gold=1000;
        var alloy=Read<GsItemCraftOk>(await Request(GameServerCommand.GS_ITEM_CRAFT_REQUEST,new GsItemCraftRequest {keyInfo=key,targetItemId=260000003}));
        Check(p.Inventory[260000003]==1&&p.Gold==950&&alloy.userItemList.Count==3,"Native material combine creates original alloy for three copper, three iron and 50 gold");
        p.Inventory.Clear();p.Inventory[260000001]=3;await Reject(GameServerCommand.GS_ITEM_CRAFT_REQUEST,new GsItemCraftRequest {keyInfo=key,targetItemId=260000003},"Missing second ingredient after first recipe input");
        await Reject(GameServerCommand.GS_ITEM_CRAFT_REQUEST,new GsItemCraftRequest {keyInfo=key,targetItemId=120101004},"Gem target sent to material-craft endpoint");
        // Cover all actual same-version paths rather than a few hardcoded IDs.
        int refineCount=0,awakeCount=0,gemCount=0;
        foreach(var source in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT)) {
            foreach(bool awaken in new[]{false,true}) {
                int next=awaken?source.AwakningNextId:source.Next1Id;if(next==0)continue;
                var instance=Grant(source);Materials(source,awaken);uint before=p.Gold;
                Console.WriteLine($"Crafting path {source.Id} -> {next}; awakening={awaken}");
                if(awaken)await Request(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,Awake(instance));else await Request(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,Refine(instance));
                Check(instance.ItemId==next&&instance.RegisteredAt!=0&&p.Gold<=before,$"Original {(awaken?"awakening":"refinement")} path {source.Id} -> {next}");
                p.EquipmentItems.Remove(instance);
                if(awaken)awakeCount++;else refineCount++;
            }
        }
        foreach(var source in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Next1Id!=0&&x.Step<c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ITEM_CRAFT_GEM))) {
            p.Inventory.Clear();for(int i=0;i<source.Material1Id.Count;i++)p.Inventory[(uint)source.Material1Id[i]]=source.Material1Value[i]+(i==0?1:0);
            p.Gold=uint.MaxValue;await Request(GameServerCommand.GS_GEM_CRAFT_REQUEST,Gem((uint)source.Id));
            Check(p.Inventory.Count==1&&p.Inventory[(uint)source.Next1Id]==1,$"Original gem recipe {source.Id} -> {source.Next1Id}, including multi-color high-tier materials");gemCount++;
        }
        server.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.EquipmentItems.First(x=>x.Sequence==gear.Sequence).ItemId==gear.ItemId&&restored.CardStates[1].GemIds.SequenceEqual(p.CardStates[1].GemIds)&&restored.CraftingReceipts.Count>0,"Refinement, awakenings, sockets and replay receipts survive disk reload");
        var inv=Read<GsGetUserItemListOk>(await Request(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest {keyInfo=key}));
        Check(inv.UserItemList.Single(x=>x.itemSeq==gear.Sequence).itemID==gear.ItemId&&inv.UserItemList.Single(x=>x.itemSeq==gear.Sequence).cardSeq==1,"Fresh inventory query reports the upgraded worn equipment with one instance");
        Check(inv.UserItemList.Where(x=>c.Items.First(i=>i.Id==x.itemID).ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).All(x=>x.itemSeq==0),"Login inventory indexes all materials and gems as native zero-serial stackables");
        Check(hashes.All(x=>SHA256.HashData(File.ReadAllBytes(x.Key)).SequenceEqual(x.Value)),"All production and shared profile saves remain byte-identical");
        File.WriteAllText(Path.Combine(root,"evidence/crafting-protocol-test.json"),JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,refineCount,awakeCount,gemCount,checks},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"Crafting protocol: {checks.Count}/{checks.Count}; original paths refinement={refineCount}, awakening={awakeCount}, gem={gemCount}");
    }
}
