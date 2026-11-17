using System.Net.Sockets;
using System.Text.Json;

internal static class BatchEquipmentTest
{
    private sealed class Peer(string root) : IDisposable
    {
        private readonly TcpClient socket=new();
        private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        public SessionKeyInfo Key=new();public byte[] Last=[];
        private async Task<(ushort Command,byte[] Payload)> Send(TcpClient client,ushort cmd,object request)
        {
            await client.GetStream().WriteAsync(codec.Encode(cmd,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);
            var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);
        }
        public async Task Login(LocalRuntime server)
        {
            using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
            await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});
            var start=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),(await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Payload)!;
            Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);
        }
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true)
        {
            Key.sequenceNo++;if(body is LocalEquipmentRequest r)r.keyInfo=Key;
            var result=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)result.Command).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)result.Command+": "+((FailPacket)WireCodec.Deserialize(typeof(FailPacket),result.Payload)!).message);
            Last=result.Payload;return(T)WireCodec.Deserialize(typeof(T),Last)!;
        }
        public Task<LocalEquipmentReply> Batch(LocalEquipmentRequest body)=>Request<LocalEquipmentReply>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,body);
        public void Dispose()=>socket.Dispose();
    }
    private static bool Basic(BscTable.ItemData i)=>i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&i.Extraction&&i.PrevId==0&&i.Step<=1&&i.EquipmentType<=BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL&&i.Grade<BscTable.ITEM_GRADE.ITEM_GRADE_HERO;
    internal static void Prepare(LocalRuntime r)
    {
        var p=r.Store.GetOrCreate("local-player");var c=r.Catalog;p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=123456;p.CashPoint=654321;p.ClearStage=179;
        p.CardIds[0]=20103;p.CardStates[1]=new(){Level=99};p.Inventory.Clear();p.EquipmentItems.Clear();
        var items=c.Items.Where(Basic).OrderBy(x=>x.ItemSubType).ThenBy(x=>x.Id).ToArray();if(items.Length==0)throw new Exception("No original base equipment");
        for(int i=0;i<27;i++)p.EquipmentItems.Add(new(){ItemId=(uint)items[(i/3)%items.Length].Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        p.CardStates[1].WeaponSequence=p.EquipmentItems[0].Sequence;p.EquipmentItems[1].Locked=true;
        var advanced=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Extraction&&x.Step>1);
        p.EquipmentItems.Add(new(){ItemId=(uint)advanced.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now,CurrentEmpowerValue=5,EmpowerValues=[24,0,0,0,0,0,0,0,0,0,0,0]});
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;r.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);
        string allowed=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);if(r.Store.All().Count==0)Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(12));
    }
    private static LocalEquipmentRequest Req(uint operation,params ulong[] seq)=>new(){operation=operation,sequences=seq.ToList(),transactionId=Guid.NewGuid().ToString("N")};
    public static async Task Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        string save=Path.Combine(root,"verification/batch-equipment",Guid.NewGuid().ToString("N"));LocalEquipmentRequest? completed=null;byte[] completedReply=[];ulong remaining=0,lockedSeq=0;
        await using(var r=new LocalRuntime(root,save,0,0,0,false)) {
            Prepare(r);using var peer=new Peer(root);await peer.Login(r);var p=r.Store.Find(peer.Key.uid)!;var c=r.Catalog;
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Inventory,p.EquipmentItems,p.CardStates,p.EquipmentBatchReceipts,p.EquipmentReceipts,p.ShopReceipts});
            async Task Bad(LocalEquipmentRequest body,string label){string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,body,false);Check(before==State(),label+" rejects atomically");}
            var ids=p.EquipmentItems.Select(x=>x.Sequence).ToArray();lockedSeq=ids[1];remaining=ids[26];
            var state=await peer.Batch(Req(LocalEquipmentRequest.State,ids));Check(state.locked.SequenceEqual(new[]{lockedSeq}),"Original equipped and bag instances expose only their own persisted locks");
            Check(((GsExtractEquipmentItemOk)WireCodec.Deserialize(typeof(GsExtractEquipmentItemOk),WireCodec.Serialize(state))!).userCoinInfo.gold==p.Gold,"Extended response retains original protobuf fields");
            await Bad(Req(LocalEquipmentRequest.Extract),"Empty selection");await Bad(Req(LocalEquipmentRequest.Preview,ids[2],ids[2]),"Duplicate unique serial");await Bad(Req(LocalEquipmentRequest.Preview,0),"Zero serial");await Bad(Req(LocalEquipmentRequest.Preview,ids[2],ulong.MaxValue),"Mixed owned and foreign serials");await Bad(Req(99,ids[2]),"Unknown batch operation");
            var tooMany=Req(LocalEquipmentRequest.State,Enumerable.Range(1,201).Select(x=>(ulong)x).ToArray());await Bad(tooMany,"Oversized batch");
            await Bad(Req(LocalEquipmentRequest.Preview,ids[0],ids[2]),"Worn equipment in mixed batch");await Bad(Req(LocalEquipmentRequest.Preview,ids[1],ids[2]),"Locked equipment in mixed batch");await Bad(Req(LocalEquipmentRequest.Preview,ids[27]),"Improved equipment without explicit opt-in");
            var legacy=new GsExtractEquipmentItemRequest{keyInfo=peer.Key,equipmentItemSeq=lockedSeq};string before=State();await peer.Request<FailPacket>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,legacy,false);Check(before==State(),"Legacy single extraction respects equipment locks");
            var saleItem=c.Items.First(x=>x.Id==p.EquipmentItems[1].ItemId);
            var sale=new GsSellUserItemRequest{keyInfo=peer.Key};sale.sellItemInfo.Add(new(){itemID=(uint)saleItem.Id,itemSeq=lockedSeq,itemCount=1});before=State();await peer.Request<FailPacket>(GameServerCommand.GS_SELL_USER_INVENTORY_ITEM_REQUEST,sale,false);Check(before==State(),"Legacy sale cannot bypass locked equipment");
            var lockRequest=Req(LocalEquipmentRequest.Lock,ids[2]);await peer.Batch(lockRequest);Check(p.EquipmentItems.Single(x=>x.Sequence==ids[2]).Locked,"Lock protects the selected unique copy");before=State();var lockBytes=peer.Last.ToArray();await peer.Batch(lockRequest);Check(before==State()&&lockBytes.SequenceEqual(peer.Last),"Lock retry with a new packet sequence is idempotent");
            lockRequest.operation=LocalEquipmentRequest.Unlock;await Bad(lockRequest,"Reusing transaction ID for another action");await peer.Batch(Req(LocalEquipmentRequest.Unlock,ids[2]));
            var preview=Req(LocalEquipmentRequest.Preview,ids[2],ids[3]);before=State();var quote=await peer.Batch(preview);Check(before==State()&&quote.tiers.Sum(x=>x.count)==2&&quote.previewToken.Length==64,"Preview grants nothing and reports exact stone tiers with an instance snapshot");
            var extract=Req(LocalEquipmentRequest.Extract,ids[2],ids[3]);extract.previewToken=quote.previewToken;
            p.EquipmentItems.Single(x=>x.Sequence==ids[3]).CurrentEmpowerValue=5;extract.allowImproved=true;await Bad(extract,"Equipment upgraded after preview");p.EquipmentItems.Single(x=>x.Sequence==ids[3]).CurrentEmpowerValue=0;extract.allowImproved=false;
            await peer.Batch(Req(LocalEquipmentRequest.Lock,ids[3]));await Bad(extract,"Equipment locked after preview");await peer.Batch(Req(LocalEquipmentRequest.Unlock,ids[3]));
            p.CardStates[1].AccessorySequences[0]=ids[3];await Bad(extract,"Equipment worn after preview");p.CardStates[1].AccessorySequences[0]=0;
            p.ActiveStage=c.FirstAdventureStage;await Bad(extract,"Extraction during battle");await Bad(Req(LocalEquipmentRequest.Lock,ids[2]),"Lock change during battle");p.ActiveStage=null;
            var pool=new EngravingRules(c).ExtractionPool(c.Items.First(x=>x.Id==p.EquipmentItems[2].ItemId)).ToArray();p.Inventory[(uint)pool[0].Id]=999999999;await Bad(extract,"Worst-case random reward stack overflow");p.Inventory.Clear();
            var ingredients=c.Items.Where(x=>x.ItemType is BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT or BscTable.ITEM_TYPE.ITEM_TYPE_EMPOWERSTONE).Where(x=>pool.All(s=>s.Id!=x.Id)).Take(1000).ToArray();
            if(ingredients.Length==1000){foreach(var item in ingredients)p.Inventory[(uint)item.Id]=1;await Bad(extract,"Material inventory slot overflow");p.Inventory.Clear();}
            quote=await peer.Batch(preview);extract.previewToken=quote.previewToken;uint gold=p.Gold,cash=p.CashPoint;
            var result=await peer.Batch(extract);completed=extract;completedReply=peer.Last.ToArray();Check(p.EquipmentItems.All(x=>x.Sequence!=ids[2]&&x.Sequence!=ids[3])&&p.EquipmentItems.Any(x=>x.Sequence==ids[4]),"Batch removes only the two selected instances, preserving identical copies");
            Check(p.Inventory.Sum(x=>x.Value)==2&&p.Gold==gold&&p.CashPoint==cash,"Two extractions grant exactly two stones without charging or refunding currency");
            Check(result.deltaItemList.Count(x=>x.itemCount==-1)==2&&result.deltaItemList.Where(x=>x.itemCount>0).Sum(x=>x.itemCount)==2,"Native inventory delta and aggregate rewards are exact");
            before=State();await peer.Batch(extract);Check(before==State()&&peer.Last.SequenceEqual(completedReply),"Repeated batch returns identical random result without a second mutation");
            extract.sequences.Add(ids[4]);await Bad(extract,"Changed retry selection");extract.sequences.RemoveAt(extract.sequences.Count-1);
            await Bad(Req(LocalEquipmentRequest.Preview,ids[2]),"Deleted instance cannot be selected again");
            var advanced=Req(LocalEquipmentRequest.Preview,ids[27]);advanced.allowImproved=true;quote=await peer.Batch(advanced);advanced.operation=LocalEquipmentRequest.Extract;advanced.previewToken=quote.previewToken;await peer.Batch(advanced);Check(p.EquipmentItems.All(x=>x.Sequence!=ids[27]),"Explicitly confirmed upgraded gear can be disassembled");
            // The original one-at-a-time client stays compatible.
            legacy.equipmentItemSeq=ids[4];var one=await peer.Request<GsExtractEquipmentItemOk>(GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST,legacy);Check(one.deltaItemList.Count(x=>x.itemCount<0)==1&&p.EquipmentItems.All(x=>x.Sequence!=ids[4]),"Original APK single-equipment packet still works");
            var batch=new List<ulong>();for(int i=0;i<200;i++){var gear=new LocalOwnedEquipment{ItemId=(uint)c.Items.First(Basic).Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(gear);batch.Add(gear.Sequence);}
            var full=Req(LocalEquipmentRequest.Preview,batch.ToArray());quote=await peer.Batch(full);full.operation=LocalEquipmentRequest.Extract;full.previewToken=quote.previewToken;result=await peer.Batch(full);
            Check(result.deltaItemList.Count(x=>x.itemCount<0)==200&&result.deltaItemList.Where(x=>x.itemCount>0).Sum(x=>x.itemCount)==200&&peer.Last.Length+WireCodec.HeaderSize<=8192,"Full 200-item encrypted batch fits the original frame limit and aggregates all rewards");
            p.EquipmentItems.Single(x=>x.Sequence==remaining).ExtraFields=new(){["futureEquipmentField"]=JsonSerializer.SerializeToElement(123)};r.Store.Save();
        }
        await using(var r=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(r);var p=r.Store.Find(peer.Key.uid)!;
            Check(p.EquipmentItems.Single(x=>x.Sequence==lockedSeq).Locked&&p.EquipmentItems.Single(x=>x.Sequence==remaining).ExtraFields!["futureEquipmentField"].GetInt32()==123,"Locks and unknown future equipment fields survive restart");
            string StableState()=>JsonSerializer.Serialize(new{p.Inventory,p.EquipmentItems,p.Gold,p.CashPoint,p.EquipmentBatchReceipts});
            string before=StableState();await peer.Batch(completed!);Check(StableState()==before,"Restarted batch retry does not alter inventory, currencies or mutation receipts");
            Check(peer.Last.SequenceEqual(completedReply),"Completed transaction returns identical rewards after server restart and a new login session");
            var state=await peer.Batch(Req(LocalEquipmentRequest.State,lockedSeq,remaining));Check(state.locked.SequenceEqual(new[]{lockedSeq}),"Fresh login refreshes the persisted lock state");
        }
        File.WriteAllText(Path.Combine(root,"evidence/batch-equipment-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Batch equipment: {checks.Count}/{checks.Count}");
    }
}
