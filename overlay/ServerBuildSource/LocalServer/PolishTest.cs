using System.Net.Sockets;
using System.Text.Json;
internal static class PolishTest
{
    private sealed class Peer : IDisposable
    {
        private readonly TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(TcpClient client,ushort command,object body){await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(body),true));using var timeout=new CancellationTokenSource(10000);var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);}
        public async Task Login(LocalRuntime server){using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);}
        public async Task<T> Request<T>(GameServerCommand command,object body,bool success=true,bool retry=false){if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)command,body);string expected=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception("Expected "+expected+", got "+(GameServerCommand)r.Item1+": "+Read<FailPacket>(r.Item2).message);return Read<T>(r.Item2);}
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] b)=>(T)WireCodec.Deserialize(typeof(T),b)!;
    public static void Prepare(LocalRuntime r)
    {
        TrainingTest.Prepare(r);var c=r.Catalog;var p=r.Store.GetOrCreate("local-player");p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=100000000;p.CashPoint=1000000;p.Moonstone=100000;
        var hero=c.UsableHeroes.First(x=>(int)x.Class==1&&x.Rank==5);p.CardIds[0]=(uint)hero.Id;p.CardIds[5]=(uint)hero.Id;
        p.CardStates[1]=new(){Level=5,Exp=ProgressionRules.CardFloor(c,5),Enchant=1,ActiveSkills=[3,1],PassiveSkills=[2,1,1,1,1]};p.CardStates[6]=new(){Level=4,Exp=ProgressionRules.CardFloor(c,4)};
        p.Inventory[280000008]=100;p.Inventory[280000004]=1000;
        foreach(int id in c.FormationExpenses.SelectMany(x=>x.RequireItemId).Where(x=>x>0).Distinct())p.Inventory[(uint)id]=1000;
        // Put the universal shard before class shards to detect the old early-break UI bug.
        foreach(var item in c.Items.Where(x=>x.GetExp>0&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_ALMIGHTY).Take(1))p.Inventory[(uint)item.Id]=1000;
        foreach(var cls in new[]{1,7}){var piece=c.Pieces.First(x=>c.Cards.Any(card=>card.Id==x.NextId&&(int)card.Class==cls)&&c.Items.Any(item=>item.Id==x.ItemId&&item.GetExp>0));p.Inventory[(uint)piece.ItemId]=1000;}
        r.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification folder required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);if(r.Store.All().Count==0)Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/polish",Guid.NewGuid().ToString("N"));await using var runtime=new LocalRuntime(root,save,0,0,0,false);Prepare(runtime);
        using var peer=new Peer(root);await peer.Login(runtime);var p=runtime.Store.Find(peer.Key.uid)!;var c=runtime.Catalog;
        var checks=new List<string>();void Check(bool b,string label){if(!b)throw new Exception(label);checks.Add(label);}
        string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Moonstone,p.Inventory,p.CardStates,p.FormationLevels,p.FormationLoadouts,p.ClassRelicLevels,p.PolishReceipts});
        async Task Bad(GameServerCommand cmd,object body,string label){string old=State();await peer.Request<FailPacket>(cmd,body,false);Check(State()==old,label+" rejects atomically");}
        var reset=new LocalCardResetRequest{keyInfo=peer.Key,cardId=p.CardIds[0],cardSeq=1};var activate=new GsActiveFormationRequest{keyInfo=peer.Key,formationId=1};var upgrade=new GsEnchantFormationRequest{keyInfo=peer.Key,formationId=1};var relic=new GsClassRelicEnchantRequest{keyInfo=peer.Key,enchantClass=1};
        p.Level=29;await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Reset original level-30 gate");p.Level=59;await Bad(GameServerCommand.GS_ACTIVE_FORMATION_REQUEST,activate,"Formation original level-60 gate");p.Level=69;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Relic original level-70 gate");p.Level=99;
        await Bad(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade,"Upgrading unactivated formation");activate.formationId=999;await Bad(GameServerCommand.GS_ACTIVE_FORMATION_REQUEST,activate,"Unknown formation");activate.formationId=1;
        uint gold=p.Gold;await peer.Request<GsActiveFormationOk>(GameServerCommand.GS_ACTIVE_FORMATION_REQUEST,activate);Check(p.FormationLevels[1]==1&&p.Gold==gold-80000,"Activation charges recovered 80000 gold and grants level one");
        string before=State();await peer.Request<GsActiveFormationOk>(GameServerCommand.GS_ACTIVE_FORMATION_REQUEST,activate,retry:true);Check(State()==before,"Activation retransmission cannot charge twice");await Bad(GameServerCommand.GS_ACTIVE_FORMATION_REQUEST,activate,"Already activated formation");
        p.Inventory[280000004]=0;await Bad(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade,"Missing formation scrolls");p.Inventory[280000004]=1000;
        var f=await peer.Request<GsEnchantFormationOk>(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade);Check(f.formationLevel==2&&p.Inventory[280000004]==998&&p.Gold==gold-160000,"Upgrade consumes table gold and two scrolls");
        before=State();await peer.Request<GsEnchantFormationOk>(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade,retry:true);Check(State()==before,"Formation upgrade is idempotent");
        for(int i=2;i<40;i++)await peer.Request<GsEnchantFormationOk>(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade);Check(p.FormationLevels[1]==40,"All original formation upgrade rows reach level 40");await Bad(GameServerCommand.GS_ENCHANT_FORMATION_REQUEST,upgrade,"Formation maximum cannot bypass free sentinel row");
        var use=new GsUsingFormationRequest{keyInfo=peer.Key,formationUsingInfo=new(){gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP}};use.formationUsingInfo.formationId.AddRange([1,0,0]);use.formationUsingInfo.defenceFormationId.AddRange([0,1,0]);
        await peer.Request<GsUsingFormationOk>(GameServerCommand.GS_USING_FORMATION_REQUEST,use);Check(p.FormationLoadouts[BscTable.GAME_MODE.MODE_ARENA_PVP].Defense[1]==1,"Independent offense/defense deck selections persist");
        use.formationUsingInfo.formationId[0]=2;await Bad(GameServerCommand.GS_USING_FORMATION_REQUEST,use,"Unowned formation loadout");use.formationUsingInfo.formationId[0]=1;use.formationUsingInfo.gameMode=BscTable.GAME_MODE.MODE_NORMAL;await Bad(GameServerCommand.GS_USING_FORMATION_REQUEST,use,"PvP formation on adventure mode");use.formationUsingInfo.gameMode=BscTable.GAME_MODE.MODE_ARENA_PVP;use.formationUsingInfo.formationId.Add(1);await Bad(GameServerCommand.GS_USING_FORMATION_REQUEST,use,"Fourth formation deck");
        var list=await peer.Request<GsGetFormationListOk>(GameServerCommand.GS_GET_FORMATION_LIST_REQUEST,new GsGetFormationListRequest{keyInfo=peer.Key});Check(list.formationInfoList.Single().level==40,"Formation query returns saved levels");
        gold=p.Gold;await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic);Check(p.ClassRelicLevels[1]==1&&p.Gold==gold-200000,"Class activation uses original gold price");
        var piece=c.Pieces.First(x=>c.Cards.Any(card=>card.Id==x.NextId&&(int)card.Class==1)&&c.Items.Any(item=>item.Id==x.ItemId&&item.GetExp>0));var item=c.Items.First(x=>x.Id==piece.ItemId);int qty=(50+item.GetExp-1)/item.GetExp;
        await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Empty shard upgrade");relic.consumeItemVec.Add(new(){itemID=(uint)piece.ItemId,itemCount=qty});
        p.Moonstone=0;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Missing moonstone");p.Moonstone=100000;
        relic.consumeItemVec[0].itemCount=-1;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Negative fragments");relic.consumeItemVec[0].itemCount=qty;
        var wrong=c.Pieces.First(x=>c.Cards.Any(card=>card.Id==x.NextId&&(int)card.Class==7)&&p.Inventory.ContainsKey((uint)x.ItemId));relic.consumeItemVec[0].itemID=(uint)wrong.ItemId;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Other class shards");relic.consumeItemVec[0].itemID=(uint)piece.ItemId;
        gold=p.Gold;int held=p.Inventory[(uint)piece.ItemId];await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic);Check(p.ClassRelicLevels[1]==2&&p.Gold==gold-205000&&p.Moonstone==99999&&p.Inventory[(uint)piece.ItemId]==held-qty,"Relic level two consumes exact shards, original gold and one moonstone");
        before=State();await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,retry:true);Check(State()==before,"Relic retry cannot double consume");
        p.ClassRelicLevels[1]=30;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Relic maximum level");p.ClassRelicLevels[1]=2;
        var relicInfo=await peer.Request<GsGetClassRelicInfoOk>(GameServerCommand.GS_GET_CLASS_RELIC_INFO_REQUEST,new GsGetClassRelicInfoRequest{keyInfo=peer.Key});Check(relicInfo.classRelicLevelVec.Count==8&&relicInfo.classRelicLevelVec[1]==2&&relicInfo.relicPoint==p.Moonstone,"Original indexed relic vector and currency are returned");
        relic.enchantClass=0;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"None-class relic");relic.enchantClass=8;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"All-classes sentinel relic");relic.enchantClass=1;
        relic.consumeItemVec.Add(new(){itemID=(uint)piece.ItemId,itemCount=qty});await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Duplicate material rows");relic.consumeItemVec.RemoveAt(1);
        var universal=c.Items.First(x=>x.GetExp>0&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_ALMIGHTY);p.Inventory[(uint)universal.Id]=100000;p.Moonstone=1000000;
        for(int cls=2;cls<=7;cls++){relic.enchantClass=(uint)cls;await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Activation with unnecessary fragments");relic.consumeItemVec.Clear();await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic);Check(p.ClassRelicLevels[cls]==1,$"Original class {cls} activates at its own indexed slot");relic.consumeItemVec.Add(new(){itemID=(uint)universal.Id,itemCount=(50+universal.GetExp-1)/universal.GetExp});await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic);Check(p.ClassRelicLevels[cls]==2,$"Universal fragments upgrade class {cls}");}
        relic.enchantClass=1;
        for(int level=3;level<=30;level++){var row=c.Relics.First(x=>(int)x.Class==1).InfoList.Single(x=>x.Level==level);relic.consumeItemVec[0].itemCount=(row.NeedExp+universal.GetExp-1)/universal.GetExp;gold=p.Gold;uint moon=p.Moonstone;await peer.Request<GsClassRelicEnchantOk>(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic);Check(p.ClassRelicLevels[1]==level&&p.Gold==gold-row.Cost&&p.Moonstone==moon-row.NeedRelicPoint,$"Relic level {level} follows original expense row");}
        await Bad(GameServerCommand.GS_CLASS_RELIC_ENCHANT_REQUEST,relic,"Earned relic maximum");p.ClassRelicLevels[1]=2;
        reset.cardSeq=0;await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Ambiguous legacy duplicate-card reset");reset.cardSeq=1;
        reset.cardSeq=999;await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Unowned reset sequence");reset.cardSeq=1;reset.cardId=p.CardIds[1];await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Mismatched template and selected instance");reset.cardId=p.CardIds[0];
        p.Inventory[280000008]=0;await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Missing reset ticket");p.Inventory[280000008]=100;
        var weapon=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT);var owned=new LocalOwnedEquipment{ItemId=(uint)weapon.Id,Sequence=p.NextEquipmentSequence++};p.EquipmentItems.Add(owned);p.CardStates[1].WeaponSequence=owned.Sequence;
        var gem=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM);p.CardStates[1].GemIds[0]=(uint)gem.Id;int gemHeld=p.Inventory.GetValueOrDefault((uint)gem.Id);int expHeld=p.Inventory.GetValueOrDefault(210002001u);
        var outcome=await peer.Request<GsCardResetOk>(GameServerCommand.GS_CARD_RESET_REQUEST,reset);Check(outcome.resetCardInfo.cardSeq==1&&p.CardStates[1].Level==1&&p.CardStates[6].Level==4,"Reset changes only the selected duplicate");
        Check(p.CardStates[1].Exp==0&&p.CardStates[1].ActiveSkills.All(x=>x==1)&&p.CardStates[1].PassiveSkills.All(x=>x==1)&&p.CardStates[1].Enchant==1&&p.CardIds[0]==reset.cardId,"Reset synchronizes EXP and skills while preserving identity, stars and enchant");
        Check(p.Inventory[210002001]==expHeld+outcome.paybackLevel_Exp&&outcome.paybackLevel_Exp==c.CardResets.Single(x=>x.Level==5).CardLevelPaybackExp,"Level refund follows recovered table and credits growth once");
        Check(p.CardStates[1].WeaponSequence==0&&p.CardStates[1].GemIds.All(x=>x==0)&&p.Inventory[(uint)gem.Id]==gemHeld+1&&p.EquipmentItems.Count(x=>x.Sequence==owned.Sequence)==1,"Reset returns gems and unequips equipment without duplicating instance");
        Check(outcome.unequipItemList.Single(x=>x.itemSeq==owned.Sequence).itemCount==0,"Returned equipment response updates metadata without adding a second copy");
        before=State();await peer.Request<GsCardResetOk>(GameServerCommand.GS_CARD_RESET_REQUEST,reset,retry:true);Check(State()==before,"Reset retry cannot duplicate refunds");await Bad(GameServerCommand.GS_CARD_RESET_REQUEST,reset,"Baseline reset cannot farm rewards");
        p.CardStates[1].PassiveSkills[0]=2;await peer.Request<GsCardResetOk>(GameServerCommand.GS_CARD_RESET_REQUEST,reset);Check(p.CardStates[1].PassiveSkills[0]==1,"Skill-only reset works even at card level one");
        var legacy=await peer.Request<GsCardResetOk>(GameServerCommand.GS_CARD_RESET_REQUEST,new GsCardResetRequest{keyInfo=peer.Key,cardId=p.CardIds[6]});Check(legacy.resetCardInfo.cardSeq==7&&p.CardStates[7].Level==1,"Original unextended request remains compatible when its template is unique");
        runtime.Store.Save();var loaded=new PlayerStore(save,c).Find(p.Uid)!;Check(loaded.FormationLevels[1]==40&&loaded.ClassRelicLevels[1]==2&&loaded.FormationLoadouts[BscTable.GAME_MODE.MODE_ARENA_PVP].Defense[1]==1&&loaded.CardStates[1].Level==1&&loaded.Inventory.SequenceEqual(p.Inventory),"All three systems and inventory survive a disk reload");
        File.WriteAllText(Path.Combine(root,"evidence/polish-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("POLISH_PASS "+checks.Count);
    }
}
