using System.Net.Sockets;
using System.Text.Json;

internal static class EngravingTest
{
    private sealed class Peer : IDisposable
    {
        private readonly TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();public byte[] Last=[];
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(TcpClient client,ushort command,object request) {
            await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server) {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false) {
            if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)command,body);
            string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)r.Item1+": "+Read<FailPacket>(r.Item2).message);
            Last=r.Item2;return Read<T>(r.Item2);
        }
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    private static void Initialize(LocalPlayer p,GameCatalog c)
    {
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=100000000;p.ClearStage=179;
        p.CardIds[0]=20103;p.CardStates[1]=new(){Level=99};
        var hero=c.Cards.First(x=>x.Id==20103);
        var basic=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.Step==5&&x.CardId==0&&x.Extraction&&x.Empower);
        foreach(var item in new[]{basic,basic})p.EquipmentItems.Add(new(){ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        p.CardStates[1].WeaponSequence=p.EquipmentItems[0].Sequence;
        foreach(var item in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE))p.Inventory[(uint)item.Id]=20;
        p.Inventory[260001001]=9;p.Inventory[260002001]=0;p.Inventory[260001002]=20;
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated engraving fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){var p=runtime.Store.GetOrCreate("local-player");Initialize(p,runtime.Catalog);runtime.Store.Save();}
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/engraving",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
        ulong worn=1UL<<40;
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server);var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;Initialize(p,c);
            var gear=p.EquipmentItems[0];var bag=p.EquipmentItems[1];var data=c.Items.First(x=>x.Id==gear.ItemId);
            var rules=new EngravingRules(c);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.Inventory,p.EquipmentItems,p.CardStates,p.EquipmentReceipts});
            async Task Bad(GameServerCommand cmd,object request,string label,bool retry=false){string before=State();await peer.Request<FailPacket>(cmd,request,false,retry);Check(before==State(),label+" rejects atomically");}
            GsEmpowerEquipmentItemRequest Empower(ulong seq,params uint[] ids){var r=new GsEmpowerEquipmentItemRequest{keyInfo=peer.Key,equipmentItemSeq=seq};r.empowerStoneItemIdList.AddRange(ids);return r;}
            GsResetEmpowerEquipmentItemRequest Reset(ulong seq)=>new(){keyInfo=peer.Key,equipmentItemSeq=seq};
            GsExtractEquipmentItemRequest Extract(ulong seq)=>new(){keyInfo=peer.Key,equipmentItemSeq=seq};
            var request=Empower(worn,260001001,260001001,260001002);uint gold=p.Gold;
            p.Level=21;await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,request,"Original level-22 engraving gate");p.Level=99;
            var result=await peer.Request<GsEmpowerEquipmentItemOk>(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,request);var receipt=peer.Last.ToArray();
            Check(gear.CurrentEmpowerValue==15&&gear.EmpowerValues[0]==48&&gear.EmpowerValues[1]==4&&p.Gold==gold-1500&&p.Inventory[260001001]==7&&p.Inventory[260001002]==19,"Mixed and repeated stones consume exact counts and original gold; HP and attack use original indexes");
            Check(result.deltaItemList.Single(x=>x.itemSeq==worn).itemCount==0&&result.deltaItemList.Single(x=>x.itemSeq==worn).cardSeq==1&&result.deltaItemList.Single(x=>x.itemSeq==worn).empowerList.Count==12,"Engraving updates worn metadata without duplicating inventory instance");
            string state=State();await peer.Request<GsEmpowerEquipmentItemOk>(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,request,retry:true);Check(state==State()&&peer.Last.SequenceEqual(receipt),"Engraving retry returns identical packet with no second debit");
            request.empowerStoneItemIdList.Add(260001001);await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,request,"Modified successful sequence",true);
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,280011001,280011001,280011001,280011001,280011001,280011001),"Equipment cap");
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(999,260001001),"Foreign equipment serial");
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn),"Empty stone selection");
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,120101001),"Gem submitted as engraving stone");
            p.Gold=0;await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,260001001),"Insufficient gold");p.Gold=gold;
            p.Inventory[260001001]=1;await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,260001001,260001001),"Repeated stone count exceeds inventory");p.Inventory[260001001]=7;
            p.ActiveStage=c.FirstAdventureStage;await Bad(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,Reset(worn),"Reset during active battle");p.ActiveStage=null;
            await Bad(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,Extract(worn),"Disassembling equipped gear");
            var resetRequest=Reset(worn);var inventory=new Dictionary<uint,int>(p.Inventory);gold=p.Gold;
            var reset=await peer.Request<GsResetEmpowerEquipmentItemOk>(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,resetRequest);receipt=peer.Last.ToArray();
            Check(gear.CurrentEmpowerValue==0&&gear.EmpowerValues.All(x=>x==0)&&p.Gold==gold&&p.Inventory.OrderBy(x=>x.Key).SequenceEqual(inventory.OrderBy(x=>x.Key)),"Original reset clears bonuses for free and does not refund spent stones");
            Check(reset.deltaItemList.Single().itemCount==0&&reset.deltaItemList.Single().empowerList.Count==12,"Reset returns zero-count metadata with all twelve zero attributes");
            state=State();await peer.Request<GsResetEmpowerEquipmentItemOk>(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,resetRequest,retry:true);Check(state==State()&&peer.Last.SequenceEqual(receipt),"Reset retry is idempotent");
            await Bad(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,Reset(worn),"Reset of unengraved gear");
            p.TrainingSlots[0]=new(){CardSequence=1,EndTime=ProtocolRouter.Now+60};
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,260001001),"Engraving equipment on a training card");p.TrainingSlots.Clear();
            p.Inventory[260001001]=100;p.Gold=1000000;
            await peer.Request<GsEmpowerEquipmentItemOk>(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,Enumerable.Repeat(260001001u,data.EmpowerMaxCost/5).ToArray()));
            Check(gear.CurrentEmpowerValue==data.EmpowerMaxCost,"Exact engraving cap is accepted");
            await Bad(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,260001001),"Additional stone beyond exact cap");
            await peer.Request<GsResetEmpowerEquipmentItemOk>(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,Reset(worn));
            int stones=0;
            foreach(var stone in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE)) {
                p.Gold=uint.MaxValue;p.Inventory[(uint)stone.Id]=1;
                var empowered=await peer.Request<GsEmpowerEquipmentItemOk>(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,(uint)stone.Id));
                Check(gear.CurrentEmpowerValue==stone.EmpowerValue&&stone.AttributeValueIdType.Select((type,i)=>gear.EmpowerValues[Array.IndexOf(EngravingRules.Attributes,type)]==(uint)stone.AttributeValueId[i]).All(x=>x),$"Original stone {stone.Id} handles every one/two/three attribute value");stones++;
                await peer.Request<GsResetEmpowerEquipmentItemOk>(GameServerCommand.GS_RESET_EMPOWER_EQUIPMENT_ITEM_REQUEST,Reset(worn));
            }
            // Preserve engraving through both kinds of existing equipment upgrade.
            p.Inventory[260001001]=20;p.Gold=uint.MaxValue;await peer.Request<GsEmpowerEquipmentItemOk>(GameServerCommand.GS_EMPOWER_EQUIPMENT_ITEM_REQUEST,Empower(worn,260001001));
            foreach(int id in data.Material1Id)p.Inventory[(uint)id]=10000;
            await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,new GsEquipmentCraftRequest{keyInfo=peer.Key,cardSeq=1,baseEquipmentSeq=worn,targetEquipmentId=(uint)data.Next1Id});
            var refined=c.Items.First(x=>x.Id==gear.ItemId);foreach(int id in refined.AwakningMaterialId)p.Inventory[(uint)id]=10000;
            await peer.Request<GsEquipmentAwakeOk>(GameServerCommand.GS_EQUIPMENT_AWAKE_REQUEST,new GsEquipmentAwakeRequest{keyInfo=peer.Key,cardSeq=1,baseEquipmentSeq=worn,targetEquipmentId=(uint)refined.AwakningNextId});
            Check(gear.CurrentEmpowerValue==5&&gear.EmpowerValues[0]==24&&p.CardStates[1].WeaponSequence==worn,"Refinement and awakening retain engraving, unique serial and wearer");
            // All real stone crafting chains, including mixed-color rank-ten recipes.
            int recipes=0;
            foreach(var source in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE&&x.Next1Id!=0)) {
                p.Inventory.Clear();p.Gold=1000000;p.Inventory[(uint)source.Id]=1;
                for(int i=0;i<source.Material1Id.Count;i++){uint id=(uint)source.Material1Id[i];p.Inventory[id]=p.Inventory.GetValueOrDefault(id)+source.Material1Value[i];}
                var craft=new GsEmpowerStoneCraftRequest{KeyInfo=peer.Key,BaseItemId=(uint)source.Id,TargetItemId=(uint)source.Next1Id};
                if(recipes==0){p.Level=25;await Bad(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,craft,"Original level-26 stone crafting gate");p.Level=99;}
                uint before=p.Gold;var crafted=await peer.Request<GsEmpowerStoneCraftOk>(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,craft);
                Check(p.Inventory.Count==1&&p.Inventory[(uint)source.Next1Id]==1&&p.Gold<before&&crafted.userItemList.All(x=>x.itemSeq==0),$"Original stone recipe {source.Id} -> {source.Next1Id} consumes selected base plus exact additional materials");
                if(recipes==0){state=State();receipt=peer.Last.ToArray();await peer.Request<GsEmpowerStoneCraftOk>(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,craft,retry:true);Check(state==State()&&peer.Last.SequenceEqual(receipt),"Stone crafting retry cannot duplicate output");}
                recipes++;
            }
            var craftBad=new GsEmpowerStoneCraftRequest{KeyInfo=peer.Key,BaseItemId=260009001,TargetItemId=270010001};p.Inventory.Clear();p.Inventory[260009001]=1;p.Inventory[260009002]=1;p.Gold=1000000;
            await Bad(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,craftBad,"Missing rank-ten auxiliary ingredient");craftBad.TargetItemId=270010002;await Bad(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,craftBad,"Unrelated stone target");
            p.Inventory.Clear();p.Gold=1000000;bag.CurrentEmpowerValue=5;bag.EmpowerValues=Enumerable.Repeat(0u,12).ToList();bag.EmpowerValues[0]=24;
            var extract=Extract(bag.Sequence);var extracted=await peer.Request<GsExtractEquipmentItemOk>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,extract);receipt=peer.Last.ToArray();
            Check(!p.EquipmentItems.Contains(bag)&&p.Inventory.Count==1&&p.Inventory.Single().Value==1&&rules.ExtractionPool(data).Any(x=>x.Id==p.Inventory.Single().Key)&&p.Gold==1000000,"Local extraction destroys only selected bag instance, grants one matching-tier stone and charges no gold or engraving refund");
            Check(extracted.deltaItemList.Single(x=>x.itemSeq==bag.Sequence).itemCount==-1&&extracted.deltaItemList.Single(x=>x.itemCount>0).itemSeq==0,"Extraction response uses original removal and reward packet shapes");
            state=State();await peer.Request<GsExtractEquipmentItemOk>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,extract,retry:true);Check(state==State()&&peer.Last.SequenceEqual(receipt),"Extraction retry cannot duplicate random rewards");
            await Bad(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,Extract(bag.Sequence),"Extracting a deleted instance");
            foreach(var rank in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Extraction).Select(x=>x.Rank).Distinct()) {
                var row=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Extraction&&x.Rank==rank);var pool=rules.ExtractionPool(row).ToArray();
                Check(pool.Length==5&&pool.All(x=>rules.Weight(x)==20)&&pool.All(x=>x.Rank==Math.Clamp(rank,1,9)),"Five equal-weight extraction outputs for original equipment rank "+rank);
            }
            var extra=new LocalOwnedEquipment{ItemId=(uint)data.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(extra);p.Inventory.Clear();foreach(var stone in rules.ExtractionPool(data))p.Inventory[(uint)stone.Id]=999999999;
            await Bad(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,Extract(extra.Sequence),"Extraction stack overflow leaves equipment intact");
            gear.ExtraFields=new(){["futureEngravingFeature"]=JsonSerializer.SerializeToElement(42)};server.Store.Save();
            Console.WriteLine($"Original stones={stones}, stone recipes={recipes}");
        }
        await using(var restarted=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(restarted);var p=restarted.Store.Find(peer.Key.uid)!;
            var inv=await peer.Request<GsGetUserItemListOk>(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest{keyInfo=peer.Key});var item=inv.UserItemList.Single(x=>x.itemSeq==worn);
            Check(item.currentEmpowerValue==5&&item.empowerList[0]==24&&item.empowerList.Count==12&&item.cardSeq==1,"Restart and fresh login retain original engraving and worn metadata");
            Check(p.EquipmentItems.Single(x=>x.Sequence==worn).ExtraFields!["futureEngravingFeature"].GetInt32()==42&&p.EquipmentReceipts.Count>0,"Future equipment fields and mutation receipts survive save update");
        }
        File.WriteAllText(Path.Combine(root,"evidence/engraving-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Equipment engraving: {checks.Count}/{checks.Count}");
    }
}
