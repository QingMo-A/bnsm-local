using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

internal static class EquipmentTest
{
    public static void WriteFixture(string root,string output)
    {
        if(!Path.GetFullPath(output).StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Dedicated verification folder required");
        var c=new GameCatalog(root);var card=c.Cards.First(x=>x.Id==20103);
        var p=new LocalPlayer {Uid=100001,Account="local-player",Level=30,Exp=(uint)c.PlayerStates.First(x=>x.Level==30).Exp,Gold=10000000,CashPoint=100000,CardIds=c.StarterCards(),Deck=[1,2,3,4,5],SkillPoint=10,SkillPointStamp=ProtocolRouter.Now};
        p.CardIds[0]=(uint)card.Id;p.CardStates[1]=new() {Level=30,Exp=(uint)c.CardLevels.First(x=>x.Level==30).Rank1};
        bool Fits(BscTable.ItemData x)=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&(x.Class==card.Class||x.Class==BscTable.CARD_CLASS.CLASS_ALL)&&x.CardId==0&&(int)x.CardCharacterType==0&&x.CardFaction is BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE&&x.CardGroupType is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE&&x.Level<=30&&x.Rank>0;
        foreach(var slot in new[]{BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_NECKLACE,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_EARRING}) {
            foreach(var item in c.Items.Where(x=>Fits(x)&&x.ItemSubType==slot&&x.Step==4).Take(slot==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON?2:1))p.EquipmentItems.Add(new() {ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
        }
        foreach(var gem in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Step==2).GroupBy(x=>x.ItemSubType).Take(4).Select(g=>g.First()))p.Inventory[(uint)gem.Id]=3;
        var material=c.CardEnchants.First(x=>x.EnchantType==card.EnchantType).MaterialDataList[0];
        for(int i=0;i<material.ItemId.Count;i++)p.Inventory[(uint)material.ItemId[i]]=material.Count[i];
        p.Inventory[(uint)card.PieceId]=c.CardRanks.First(x=>x.Rank==card.Rank).NextEVP;
        for(int i=0;i<card.RankUpMaterialItemId.Count;i++)p.Inventory[(uint)card.RankUpMaterialItemId[i]]=card.RankUpMaterialItemCount[i];
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"players.json"),JsonSerializer.Serialize(new[] {p},new JsonSerializerOptions {WriteIndented=true}));
    }
    public static async Task Run(string root)
    {
        string production=Path.Combine(root,"LocalServer/saves/players.json");
        byte[] productionHash=SHA256.HashData(File.ReadAllBytes(production));
        string save=Path.Combine(root,"verification/equipment",Guid.NewGuid().ToString("N"));
        var checks=new List<string>();
        void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        T Read<T>(byte[] data)=>(T)WireCodec.Deserialize(typeof(T),data)!;
        var codec=new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
        await using var server=new LocalRuntime(root,save,0,0,0);
        var c=server.Catalog;
        using var login=new TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);
        async Task<byte[]> Login(LoginServerCommand cmd,object req) {
            await login.GetStream().WriteAsync(codec.Encode((ushort)cmd,WireCodec.Serialize(req),true));
            using var timeout=new CancellationTokenSource(15000);
            return codec.Decode((await WireCodec.ReadFrame(login.GetStream(),timeout.Token))!,true).Payload;
        }
        await Login(LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest {userID="equipment-test",userPW="local",osType=DeviceOSType.DEVICE_PC});
        var start=Read<LsGameStartOk>(await Login(LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest {serverId=1,bAgreement=true}));
        var key=new SessionKeyInfo {uid=start.uid,loginSessionKey=start.loginSessionKey};
        var p=server.Store.Find(start.uid)!;p.Level=30;p.Gold=10000000;p.CardIds[0]=20103;
        p.CardStates[1]=new() {Level=30,Exp=(uint)c.CardLevels.First(x=>x.Level==30).Rank1};
        p.CardIds.Add(20103);ulong second=(ulong)p.CardIds.Count;p.CardStates[second]=new() {Level=30};
        using var game=new TcpClient();await game.ConnectAsync("127.0.0.1",server.GamePort);
        byte[] last=[];
        async Task<byte[]> Request(GameServerCommand cmd,object req,bool success=true,bool retry=false) {
            if(!retry)key.sequenceNo++;
            await game.GetStream().WriteAsync(codec.Encode((ushort)cmd,WireCodec.Serialize(req),true));
            using var timeout=new CancellationTokenSource(15000);
            var response=codec.Decode((await WireCodec.ReadFrame(game.GetStream(),timeout.Token))!,true);
            string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");
            if(((GameServerCommand)response.Command).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(response.Payload).message);
            last=response.Payload;return response.Payload;
        }
        GsUpdateCardEquipRequest Loadout(ulong card=1,ulong weapon=0,ulong necklace=0,ulong earring=0,uint a=0,uint b=0,uint d=0) {
            var req=new GsUpdateCardEquipRequest {keyInfo=key,cardSeq=card,weaponSeq=weapon,bUnwear=true};req.accessorySeq.AddRange([necklace,earring]);req.jewelID.AddRange([a,b,d]);req.runeID.AddRange([0u,0u,0u,0u,0u,0u]);return req;
        }
        async Task<GsUpdateCardEquipOk> Equip(GsUpdateCardEquipRequest req,bool retry=false)=>Read<GsUpdateCardEquipOk>(await Request(GameServerCommand.GS_UPDATE_CARD_EQUIP_REQUEST,req,retry:retry));
        async Task Reject(GsUpdateCardEquipRequest req,string label,bool retry=false) {
            string before=JsonSerializer.Serialize(new {p.Gold,p.Inventory,p.EquipmentItems,slots=p.CardStates});
            await Request(GameServerCommand.GS_UPDATE_CARD_EQUIP_REQUEST,req,false,retry);
            Check(before==JsonSerializer.Serialize(new {p.Gold,p.Inventory,p.EquipmentItems,slots=p.CardStates}),label+" is atomic");
        }
        ulong Grant(BscTable.ItemData item) {var owned=new LocalOwnedEquipment {ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(owned);return owned.Sequence;}
        var hero=c.Cards.First(x=>x.Id==20103);
        bool General(BscTable.ItemData x)=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT && (x.Class==BscTable.CARD_CLASS.CLASS_ALL || x.Class==hero.Class) && x.CardId==0 && x.CardCharacterType==BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE && x.CardFaction is BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE && x.CardGroupType is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE && x.Level<=30;
        var gear=new[]{BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_NECKLACE,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_EARRING}
            .Select(t=>c.Items.First(x=>General(x)&&x.ItemSubType==t&&x.Step==4&&x.Rank>0)).ToArray();
        ulong[] seq=gear.Select(Grant).ToArray();ulong spare=Grant(gear[0]);
        var gems=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM && x.Step==2).GroupBy(x=>x.ItemSubType).Take(4).Select(g=>g.First()).ToArray();
        foreach(var gem in gems)p.Inventory[(uint)gem.Id]=3;
        uint[] ids=gems.Select(x=>(uint)x.Id).ToArray();
        uint Fee(BscTable.ItemData x) {var type=x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM?BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_GEM_UNWEAR:BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_UNWEAR;var rows=c.CardProcesses.List.First(t=>t.ProcessType==type).ProcessDataList;return x.Step>0&&x.Step<=rows.Count&&(x.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT||x.Rank!=0)?(uint)rows[x.Step-1].CostGold:0;}
        File.WriteAllText(Path.Combine(root,"evidence/equipment-original-audit.json"),JsonSerializer.Serialize(new {equipmentUnlock=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_CHANGE),gear=gear.Select(x=>new {x.Id,x.Class,x.Level,x.RequireCardLevel,x.Step,fee=Fee(x)}),gems=gems.Select(x=>new {x.Id,x.Step,fee=Fee(x)}),processes=c.CardProcesses.List.Where(x=>x.ProcessType is BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_GEM_UNWEAR or BscTable.ITEM_CARD_PROCESS_TYPE.PROCESS_EQUIPMENT_UNWEAR)},new JsonSerializerOptions {IncludeFields=true,WriteIndented=true}));
        Check(seq.All(x=>x>uint.MaxValue),"Equipment keeps independent 64-bit instance serials");
        var req=Loadout(weapon:seq[0],necklace:seq[1],earring:seq[2],a:ids[0],b:ids[1],d:ids[2]);
        uint gold=p.Gold;var worn=await Equip(req);byte[] receipt=last.ToArray();
        Check(p.Gold==gold,"Initial equip and gem socket do not charge unwear fees");
        Check(worn.cardInfo.weaponSeq==seq[0]&&worn.cardInfo.accessorySeq.SequenceEqual(seq.Skip(1))&&worn.cardInfo.jewelID.SequenceEqual(ids.Take(3)),"All six native slots are returned in APK order");
        Check(worn.itemInfoList.Count(x=>x.itemCount==0&&x.cardSeq==1)==3,"Equipment response updates ownership without adding duplicate instances");
        Check(worn.itemInfoList.Count(x=>x.itemCount==-1)==3&&ids.Take(3).All(x=>p.Inventory[x]==2),"Socketed gems leave the stackable inventory exactly once");
        await Equip(req,true);Check(last.SequenceEqual(receipt)&&p.Gold==gold&&p.Inventory[ids[0]]==2,"An identical packet retry returns the saved receipt without charging or consuming twice");
        await Reject(Loadout(weapon:spare),"Changed packet reusing a successful sequence",true);
        var noop=await Equip(req);Check(noop.itemInfoList.Count==0&&p.Gold==gold,"A fresh identical loadout is a no-op");
        await Reject(Loadout(card:second,weapon:seq[0]),"Taking equipment still worn by another card");
        await Reject(Loadout(card:99999),"Unknown card");
        await Reject(Loadout(weapon:99999),"Unknown equipment serial");
        await Reject(Loadout(weapon:seq[1]),"Necklace in weapon slot");
        await Reject(Loadout(weapon:seq[0],necklace:seq[0]),"Repeated equipment instance");
        await Reject(Loadout(a:(uint)gear[0].Id),"Equipment ID in a gem slot");
        var malformed=Loadout();malformed.accessorySeq.Clear();await Reject(malformed,"Missing accessory slots");
        malformed=Loadout();malformed.jewelID.Add(0);await Reject(malformed,"Extra gem slot");
        malformed=Loadout();malformed.runeID[0]=123;await Reject(malformed,"Unsupported rune data");
        malformed=Loadout(weapon:spare);malformed.bUnwear=false;await Reject(malformed,"Destructive quick replacement");
        p.ActiveStage=c.FirstAdventureStage;await Reject(Loadout(),"Changing gear during an active battle");p.ActiveStage=null;
        Check(c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EQUIPMENT_CHANGE)==1,"This APK permits changing equipment from player level one");
        var wrongClass=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==gear[0].ItemSubType&&x.Class!=hero.Class&&x.Class!=BscTable.CARD_CLASS.CLASS_ALL);
        await Reject(Loadout(weapon:Grant(wrongClass)),"Wrong weapon profession");
        var exclusive=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==gear[0].ItemSubType&&x.CardId!=0&&x.CardId!=hero.EvolveFirstAncestor);
        await Reject(Loadout(weapon:Grant(exclusive)),"Another hero's exclusive equipment");
        var high=c.Items.First(x=>General(x)&&x.ItemSubType==gear[0].ItemSubType&&x.Level>1);
        p.CardStates[1].Level=1;await Reject(Loadout(weapon:Grant(high)),"Card level below equipment level");p.CardStates[1].Level=30;
        p.Inventory[ids[3]]=0;await Reject(Loadout(weapon:seq[0],necklace:seq[1],earring:seq[2],a:ids[3],b:ids[1],d:ids[2]),"Unowned gem");p.Inventory[ids[3]]=3;
        p.Gold=0;await Reject(Loadout(),"Insufficient unwear gold");p.Gold=gold;
        uint fee=Fee(gear[0])+Fee(gems[0]);
        var replacement=Loadout(weapon:spare,necklace:seq[1],earring:seq[2],a:ids[3],b:ids[1],d:ids[2]);
        var replaced=await Equip(replacement);
        Check(p.Gold==gold-fee,"Replacement uses the APK Step-minus-one fee rows");
        Check(p.Inventory[ids[0]]==3&&p.Inventory[ids[3]]==2,"Replaced gems return to inventory and the new gem is consumed");
        Check(replaced.itemInfoList.Any(x=>x.itemSeq==seq[0]&&x.itemCount==0&&x.cardSeq==0)&&replaced.itemInfoList.Any(x=>x.itemSeq==spare&&x.itemCount==0&&x.cardSeq==1),"Replaced equipment remains owned and its wearer metadata changes");
        var other=await Equip(Loadout(card:second,weapon:seq[0]));Check(other.cardInfo.weaponSeq==seq[0],"Detached equipment can be worn by another card");
        // Growth must preserve loadout identity, not overwrite it with empty arrays.
        var materials=c.CardEnchants.First(x=>x.EnchantType==hero.EnchantType).MaterialDataList[0];
        for(int i=0;i<materials.ItemId.Count;i++)p.Inventory[(uint)materials.ItemId[i]]=materials.Count[i];
        var enchant=Read<GsCardEnchantOk>(await Request(GameServerCommand.GS_ENCHANT_CARD_REQUEST,new GsCardEnchantRequest {keyInfo=key,targetCardSeq=1}));
        Check(enchant.enchantCardInfo.weaponSeq==spare&&enchant.enchantCardInfo.jewelID.SequenceEqual(replacement.jewelID),"Card enchant preserves equipment and socketed gems");
        p.Inventory[(uint)hero.PieceId]=c.CardRanks.First(x=>x.Rank==hero.Rank).NextEVP;
        for(int i=0;i<hero.RankUpMaterialItemId.Count;i++)p.Inventory[(uint)hero.RankUpMaterialItemId[i]]=hero.RankUpMaterialItemCount[i];
        var evolution=Read<GsCardEvolutionOk>(await Request(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,new GsCardEvolutionRequest {keyInfo=key,targetCardSeq=1}));
        Check(evolution.evolutionCardInfo.cardID==hero.RankUpCardId&&evolution.evolutionCardInfo.weaponSeq==spare&&evolution.evolutionCardInfo.jewelID.SequenceEqual(replacement.jewelID),"Evolution changes the star stage while keeping the same loadout");
        server.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;
        Check(restored.CardStates[1].WeaponSequence==spare&&restored.CardStates[1].GemIds.SequenceEqual(replacement.jewelID)&&restored.EquipmentReceipts.Count>0,"Slots and mutation receipts survive reloading the save");
        var cardList=Read<GsGetUserCardListOk>(await Request(GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST,new GsGetUserCardListRquest {keyInfo=key}));
        Check(cardList.userCardInfoList.First(x=>x.cardSeq==1).weaponSeq==spare,"Login card list includes the saved weapon");
        var inventory=Read<GsGetUserItemListOk>(await Request(GameServerCommand.GS_GET_INVENTORY_ITEM_LIST_REQUEST,new GsGetUserItemListRquest {keyInfo=key}));
        Check(inventory.UserItemList.Single(x=>x.itemSeq==spare).cardSeq==1&&inventory.UserItemList.Single(x=>x.itemSeq==seq[0]).cardSeq==second,"Login inventory reports wearers for each independent equipment instance");
        var sell=new GsSellUserCardRequest {keyInfo=key};sell.cardSeqList.Add(second);
        await Request(GameServerCommand.GS_SELL_USER_CARD_REQUEST,sell,false);Check(p.CardIds[(int)second-1]!=0,"Selling a card with gear cannot orphan or destroy its loadout");
        gold=p.Gold;uint totalFee=gear.Skip(1).Aggregate(Fee(gear[0]),(sum,x)=>sum+Fee(x))+gems.Skip(1).Take(2).Aggregate(Fee(gems[3]),(sum,x)=>sum+Fee(x));
        var empty=await Equip(Loadout());
        Check(p.Gold==gold-totalFee&&empty.cardInfo.weaponSeq==0&&empty.cardInfo.accessorySeq.All(x=>x==0)&&empty.cardInfo.jewelID.All(x=>x==0),"All six slots can be unequipped with exact fees");
        Check(ids.Take(4).All(x=>p.Inventory[x]==3),"Every socketed gem is returned after final unequip");
        Check(p.EquipmentItems.Count==seq.Length+1+3,"Wear, replace and unwear never duplicate or delete owned equipment");
        Check(SHA256.HashData(File.ReadAllBytes(production)).SequenceEqual(productionHash),"Production save bytes were not modified by verification");
        File.WriteAllText(Path.Combine(root,"evidence/equipment-protocol-test.json"),JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,checks},new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"Equipment protocol: {checks.Count}/{checks.Count} checks passed");
    }
}
