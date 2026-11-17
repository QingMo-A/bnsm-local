using System.Text.Json;

internal static class UpgradeTowerTest
{
    // Same encrypted TCP peer as the original protocol regression, with a fresh
    // login and ephemeral ports for every isolated runtime.
    private sealed class Peer : IDisposable
    {
        private readonly System.Net.Sockets.TcpClient socket=new();private readonly WireCodec codec;public SessionKeyInfo Key=new();public byte[] Last=[];
        public Peer(string root){codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);}
        private async Task<(ushort,byte[])> Send(System.Net.Sockets.TcpClient client,ushort command,object request){await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(request),true));using var timeout=new CancellationTokenSource(10000);var r=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);return(r.Command,r.Payload);}
        public async Task Login(LocalRuntime server){using var login=new System.Net.Sockets.TcpClient();await login.ConnectAsync("127.0.0.1",server.LoginPort);await Send(login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID="local-player",userPW="local",osType=DeviceOSType.DEVICE_PC});var start=Read<LsGameStartOk>((await Send(login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true})).Item2);Key=new(){uid=start.uid,loginSessionKey=start.loginSessionKey};await socket.ConnectAsync("127.0.0.1",server.GamePort);}
        public async Task<T> Request<T>(GameServerCommand cmd,object req,bool success=true,bool retry=false){if(!retry)Key.sequenceNo++;var r=await Send(socket,(ushort)cmd,req);string expected=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL");if(((GameServerCommand)r.Item1).ToString()!=expected)throw new Exception(expected+": "+Read<FailPacket>(r.Item2).message);Last=r.Item2;return Read<T>(r.Item2);}
        public void Dispose()=>socket.Dispose();
    }
    private static T Read<T>(byte[] bytes)=>(T)WireCodec.Deserialize(typeof(T),bytes)!;
    public static void Initialize(LocalPlayer p,GameCatalog c)
    {
        p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=100000000;p.ClearStage=179;p.Inventory.Clear();
        var firstBans=c.TowerBans.Single(x=>x.Id==1).CTBanCardIdInfoList.Select(x=>x.BanCardId).ToHashSet();
        var heroes=c.UsableHeroes.Where(x=>!firstBans.Contains(x.EvolveFirstAncestor)&&x.Rank==6&&x.Talent==BscTable.CARD_TALENT.CARD_TALENT_SS).GroupBy(x=>x.EvolveFirstAncestor).Select(x=>x.First()).Take(5).ToList();
        p.CardIds=heroes.Select(x=>(uint)x.Id).ToList();p.CardIds[0]=20103;p.Deck=[1,2,3,4,5];p.Decks[25]=p.Deck.ToList();
        for(ulong seq=1;seq<=5;seq++)p.CardStates[seq]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=seq==1?0u:10u,ActiveSkills=[50,50],PassiveSkills=[50,50,50,50,50]};
        var hero=c.Cards.First(x=>x.Id==20103);
        var basic=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Class==hero.Class&&x.Next1Id!=0&&x.Material1Id.Contains(260000003)&&x.CardId==0);
        p.EquipmentItems.Add(new(){ItemId=(uint)basic.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});p.CardStates[1].WeaponSequence=p.EquipmentItems[0].Sequence;
        void Leafs(uint id,int depth){if(depth>12)throw new Exception("Fixture recipe cycle");var recipe=c.Recipes.FirstOrDefault(x=>x.TargetItemId==id&&c.Items.Any(i=>i.Id==id&&i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT));if(recipe==null){p.Inventory[id]=100000;return;}foreach(int input in recipe.MeterialItemId)Leafs((uint)input,depth+1);}
        foreach(int id in basic.Material1Id)Leafs((uint)id,0);
        var cardMaterials=c.CardEnchants.First(x=>x.EnchantType==hero.EnchantType).MaterialDataList;
        int enchantIndex=cardMaterials.FindIndex(x=>x.ItemId.Any(id=>c.Recipes.Any(r=>r.TargetItemId==id)&&c.Items.Any(i=>i.Id==id&&i.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT)));
        p.CardStates[1].Enchant=(uint)Math.Max(0,enchantIndex);
        var mat=cardMaterials[(int)p.CardStates[1].Enchant];foreach(int id in mat.ItemId)Leafs((uint)id,0);
        for(ulong seq=2;seq<=5;seq++) {
            var card=c.Cards.First(x=>x.Id==p.CardIds[(int)seq-1]);
            var weapon=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON&&x.Class==card.Class&&x.CardId==0&&x.CardCharacterType==BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE).OrderByDescending(x=>x.Step).First();
            var item=new LocalOwnedEquipment{ItemId=(uint)weapon.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(item);p.CardStates[seq].WeaponSequence=item.Sequence;
            p.CardStates[seq].GemIds=[120101010,120301010,120501010];
        }
        p.Inventory[120101003]=16;p.Inventory.Remove(120101004);p.Inventory.Remove(120101005);
        ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated upgrade/tower fixture required");
        Directory.CreateDirectory(output);await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){var p=runtime.Store.GetOrCreate("local-player");Initialize(p,runtime.Catalog);
            // The native battle uses a separate, fully developed test roster.
            // Keep card one and its equipment unchanged for material-upgrade checks.
            var c=runtime.Catalog;var banned=c.TowerBans.Single(x=>x.Id==1).CTBanCardIdInfoList.Select(x=>x.BanCardId).ToHashSet();
            var champions=c.UsableHeroes.Where(x=>!banned.Contains(x.EvolveFirstAncestor)&&x.Talent>=BscTable.CARD_TALENT.CARD_TALENT_SS).GroupBy(x=>x.EvolveFirstAncestor).Select(g=>g.OrderByDescending(x=>x.Rank).First()).OrderByDescending(x=>x.Rank).Take(5).ToList();
            foreach(var card in champions) {
                p.CardIds.Add((uint)card.Id);ulong seq=(ulong)p.CardIds.Count;
                var state=new LocalCardState{Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=10,ActiveSkills=[50,50],PassiveSkills=[50,50,50,50,50],GemIds=[120101010,120301010,120501010]};p.CardStates[seq]=state;
                var weapon=c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON&&x.Class==card.Class&&x.CardId==0&&x.CardCharacterType==BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE).OrderByDescending(x=>x.Step).First();
                var gear=new LocalOwnedEquipment{ItemId=(uint)weapon.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now};p.EquipmentItems.Add(gear);state.WeaponSequence=gear.Sequence;
            }
            p.Decks[25]=[6,7,8,9,10];
            runtime.Store.Save();File.WriteAllText(Path.Combine(output,"expectations.json"),JsonSerializer.Serialize(new {equipmentTarget=runtime.Catalog.Items.Single(x=>x.Id==p.EquipmentItems[0].ItemId).Next1Id,enchantTarget=p.CardStates[1].Enchant+1}));}
        await using var app=await DebugToolsTest.Host(runtime);var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(15));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/upgrade-tower",Guid.NewGuid().ToString("N"));var checks=new List<string>();void Check(bool v,string label){if(!v)throw new Exception(label);checks.Add(label);}
        await using(var server=new LocalRuntime(root,save,0,0,0,false)) {
            using var peer=new Peer(root);await peer.Login(server);var p=server.Store.Find(peer.Key.uid)!;var c=server.Catalog;Initialize(p,c);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Inventory,p.CardStates,p.EquipmentItems,p.ChallengeTower,p.TowerReceipts,p.CraftingReceipts});
            async Task Bad(GameServerCommand cmd,object req,string label,bool retry=false){string before=State();await peer.Request<FailPacket>(cmd,req,false,retry);Check(before==State(),label+" rejects without partial material, gold, equipment or tower mutations");}
            var gear=p.EquipmentItems[0];var source=c.Items.Single(x=>x.Id==gear.ItemId);uint gold=p.Gold;
            var refine=new GsEquipmentCraftRequest{keyInfo=peer.Key,cardSeq=1,baseEquipmentSeq=gear.Sequence,targetEquipmentId=(uint)source.Next1Id};
            var refined=await peer.Request<GsEquipmentCraftOk>(GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST,refine);
            Check(gear.ItemId==source.Next1Id&&p.Gold<gold&&!p.Inventory.ContainsKey(260000003)&&refined.userItemList.Count(x=>x.itemCount<0)>=2,"Equipment upgrade composes missing alloy and upgrades in one original response");
            var gem=new GsGemCraftRequest{keyInfo=peer.Key,baseGemId=120101004,targetGemId=120101005,craftCount=1,slotIndex=2};gold=p.Gold;
            var gems=await peer.Request<GsGemCraftOk>(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem);byte[] receipt=peer.Last.ToArray();
            Check(p.Inventory.GetValueOrDefault(120101003u)==0&&p.Inventory.GetValueOrDefault(120101004u)==0&&p.Inventory[120101005]==1&&p.Gold<gold,"Gem synthesis recursively combines sixteen lower gems into four intermediate gems and one target");
            Check(gems.userItemList.Any(x=>x.itemID==120101003&&x.itemCount==-16)&&gems.userItemList.All(x=>x.itemSeq==0),"Automatic gem composition returns net leaf-material deltas for native inventory");
            string state=State();await peer.Request<GsGemCraftOk>(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem,retry:true);Check(State()==state&&peer.Last.SequenceEqual(receipt),"Combined upgrade retry does not compose or debit twice");
            p.Inventory[120101003]=15;await Bad(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem,"One missing leaf gem");
            p.Inventory[120101003]=16;p.Gold=0;await Bad(GameServerCommand.GS_GEM_CRAFT_REQUEST,gem,"Gold insufficient for the combined chain");p.Gold=1000000;
            p.Inventory[260001001]=16;p.Inventory.Remove(260002001);p.Inventory.Remove(260003001);
            var stone=new GsEmpowerStoneCraftRequest{KeyInfo=peer.Key,BaseItemId=260002001,TargetItemId=260003001};
            await peer.Request<GsEmpowerStoneCraftOk>(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,stone);
            Check(p.Inventory.GetValueOrDefault(260001001u)==0&&p.Inventory.GetValueOrDefault(260002001u)==0&&p.Inventory[260003001]==1,"Stone crafting recursively produces the missing base and extra stones without requiring manual clicks");
            p.Inventory[260001001]=15;p.Inventory.Remove(260002001);await Bad(GameServerCommand.GS_EMPOWER_STONE_CRAFT_REQUEST,stone,"Shared lower-stone shortage");
            // Tower lobby, area order, first-area opportunity, original rewards,
            // bans, death flags, restart and both reset boundaries.
            GsChallengeStageLobbyRequest Lobby()=>new(){keyInfo=peer.Key};
            GsChallengeStageBattleStartRequest Start(uint floor,uint area)=>new(){keyInfo=peer.Key,difficulty=floor,stageid=area};
            GsChallengeStageBattleEndConfirmRequest End(uint floor,uint area,bool win=true,params ulong[] dead){var r=new GsChallengeStageBattleEndConfirmRequest{keyInfo=peer.Key,difficulty=floor,stageid=area,timeStamp=p.ChallengeTower.ActiveStamp,enemyAliveCount=(uint)(p.ChallengeTower.ActiveStamp%1000)+(win?0u:1u),endType=win?NormalBattleEndType.NORMAL_BATTLE_SUCCESS:NormalBattleEndType.NORMAL_BATTLE_FAIL,cardDieCount=(uint)dead.Length,battleEndSyncData=new(){clearTime=5}};r.dieCardSeq.AddRange(dead);return r;}
            p.Level=89;await Bad(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby(),"Original level-90 tower gate");p.Level=99;
            var lobby=await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());Check(lobby.challengeAchieveInfo.Length==48&&lobby.challengePoint==0&&lobby.dieCardSeq.Count==0,"Fresh lobby includes all seven floors with original six-byte strides");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(2,1),"Skipping locked tower floor");await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,2),"Skipping uncleared region");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,End(1,1),"Settling without active tower battle");
            uint original=p.CardIds[0];p.CardIds[0]=(uint)c.Cards.First(x=>x.EvolveFirstAncestor==c.TowerBans.First(x=>x.Id==1).CTBanCardIdInfoList[0].BanCardId).Id;
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,1),"Original permanently banned card identity");p.CardIds[0]=original;
            p.TrainingSlots[0]=new(){CardSequence=1,EndTime=ProtocolRouter.Now+60};await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,1),"Training card in tower deck");p.TrainingSlots.Clear();
            var startRequest=Start(1,1);var started=await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,startRequest);state=State();receipt=peer.Last.ToArray();
            await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,startRequest,retry:true);Check(State()==state&&peer.Last.SequenceEqual(receipt)&&p.ChallengeTower.UsedEntries==0,"Starting and retrying the first area do not consume an opportunity before victory");
            var wrong=End(1,1);wrong.timeStamp++;await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,wrong,"Mismatched battle stamp");
            var badAlive=End(1,1);badAlive.enemyAliveCount++;await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,badAlive,"Original timestamp-obfuscated live-enemy counter");
            var endRequest=End(1,1);var ended=await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,endRequest);
            Check(ended.earnedChallengePoint==15&&ended.challengePoint==15&&ended.earnedItemList.Single().itemID==290000001&&ended.earnedItemList.Single().itemCount==1&&p.ChallengeTower.UsedEntries==1,"Perfect first area awards original five points times three, one original reward item and consumes one floor opportunity");
            state=State();receipt=peer.Last.ToArray();await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,endRequest,retry:true);Check(State()==state&&peer.Last.SequenceEqual(receipt),"Tower victory retry cannot duplicate points, items or opportunity use");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,1),"Replaying a rewarded region");
            await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,2));await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,End(1,2,true,1));
            Check(p.ChallengeTower.Areas[12]==2&&p.ChallengeTower.Points==35&&p.ChallengeTower.DeadCards.Contains(1),"One death gives two stars, original point multiplier and a persistent fallen flag");
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,3),"Fallen card in primary deck");p.Decks[25]=[0,2,3,4,5];
            for(uint area=3;area<=5;area++){await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,area));await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,End(1,area));}
            await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(2,1),"Second floor beyond daily opportunity");
            uint points=p.ChallengeTower.Points;p.ChallengeTower.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)-1;
            lobby=await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());Check(lobby.clearChallengeDifficulty==1&&lobby.clearChallengeStage==5&&lobby.challengePoint==points&&lobby.dieCardSeq.Count==0&&lobby.challengeAchieveInfo.All(x=>x==0)&&p.ChallengeTower.UsedEntries==0,"Daily 04:00 reset clears regions, deaths and used opportunity while preserving weekly points and unlocked floor");
            // Exercise every actual region without inventing higher content.
            p.Decks[25]=[1,2,3,4,5];int count=5;
            for(uint floor=2;floor<=7;floor++) {
                if(floor>2){p.ChallengeTower.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)-1;await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());}
                // Select five owned non-banned identities from this original floor.
                var row=c.Towers.Single(x=>x.Id==floor);var ban=c.TowerBans.Where(x=>x.Id==row.BanGroupId_Always||x.Id==row.BanGroupId_Monday||x.Id==row.BanGroupId_Tuesday||x.Id==row.BanGroupId_Wednesday||x.Id==row.BanGroupId_Thursday||x.Id==row.BanGroupId_Friday||x.Id==row.BanGroupId_Saturday||x.Id==row.BanGroupId_Sunday).SelectMany(x=>x.CTBanCardIdInfoList).Select(x=>x.BanCardId).ToHashSet();
                p.CardIds=c.UsableHeroes.Where(x=>!ban.Contains(x.EvolveFirstAncestor)).GroupBy(x=>x.EvolveFirstAncestor).Select(x=>(uint)x.First().Id).Take(5).ToList();
                for(uint area=1;area<=5;area++){var r=c.TowerStages.Single(x=>x.TowerId==floor&&x.AreaId==area);await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(floor,area));var reward=await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,End(floor,area));Check(reward.earnedChallengePoint==r.TowerPoint*3&&reward.earnedItemList.Sum(x=>x.itemCount)==r.RewardQuantity.Sum(),$"Original tower {floor} region {area} rewards and score");count++;}
            }
            Check(count==35&&p.ChallengeTower.BestFloor==7&&p.ChallengeTower.BestArea==5,"All thirty-five original tower regions are reachable across daily floor entries");
            p.ChallengeTower.Points=70001;p.ChallengeTower.Week=(ProtocolRouter.TowerDay(ProtocolRouter.Now)+3)/7-1;p.Inventory.Clear();p.Gold=0;
            lobby=await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());
            Check(lobby.challengePoint==0&&lobby.clearChallengeDifficulty==0&&lobby.challengeAchieveInfo.All(x=>x==0)&&p.ChallengeTower.PendingWeeklyRewards.Count==0&&p.Gold==400000&&p.Inventory[290000001]==25,"Monday 04:00 weekly reset grants exact original 70001-point bracket once and resets unlocked floors and score");
            state=State();await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());Check(State()==state,"Repeated lobby query cannot redeliver weekly reward");
            var allBans=c.TowerBans.SelectMany(x=>x.CTBanCardIdInfoList).Select(x=>x.BanCardId).ToHashSet();p.CardIds=c.UsableHeroes.Where(x=>!allBans.Contains(x.EvolveFirstAncestor)).GroupBy(x=>x.EvolveFirstAncestor).Select(x=>(uint)x.First().Id).Take(5).ToList();p.Decks[25]=[1,2,3,4,5];
            await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,1));
            var fakeDead=End(1,1,false,9999);await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,fakeDead,"Unowned fallen card in settlement");
            var lost=await peer.Request<GsChallengeStageBattleEndConfirmOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,End(1,1,false,1,2,3,4,5));
            Check(lost.earnedItemList.Count==0&&lost.earnedChallengePoint==0&&p.ChallengeTower.UsedEntries==0&&p.ChallengeTower.DeadCards.Count==5,"Defeat gives no rewards or entry debit and persists the actual fallen roster");
            p.ChallengeTower.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)-1;await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());
            await peer.Request<GsChallengeStageBattleStartOk>(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_START_REQUEST,Start(1,1));var stale=End(1,1);p.ChallengeTower.Day=ProtocolRouter.TowerDay(ProtocolRouter.Now)-1;
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());await Bad(GameServerCommand.GS_CHALLENGE_STAGE_BATTLE_END_REQUEST,stale,"Settlement after daily reset");
            p.ChallengeTower.Week=(ProtocolRouter.TowerDay(ProtocolRouter.Now)+3)/7-1;p.ChallengeTower.Points=70001;p.Gold=uint.MaxValue;
            await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());Check(p.ChallengeTower.PendingWeeklyRewards.Count==1&&p.Gold==uint.MaxValue,"Weekly reward waits safely when currency capacity is full");
            p.Gold=0;p.Inventory.Clear();await peer.Request<GsChallengeStageLobbyOk>(GameServerCommand.GS_CHALLENGE_STAGE_LOBBY_REQUEST,Lobby());Check(p.Gold==400000&&p.ChallengeTower.PendingWeeklyRewards.Count==0,"Pending weekly reward is granted once after capacity becomes available");
            p.ChallengeTower.ExtraFields=new(){["futureTowerFeature"]=JsonSerializer.SerializeToElement(17)};server.Store.Save();
        }
        await using(var server=new LocalRuntime(root,save,0,0,0,false)){using var peer=new Peer(root);await peer.Login(server);var p=server.Store.Find(peer.Key.uid)!;var info=await peer.Request<GsGetUserGameInfoOk>(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST,new GsGetUserGameInfoRequest{keyInfo=peer.Key});Check(info.challengeAchieveInfo.Length==48&&p.Gold==400000&&p.ChallengeTower.ExtraFields!["futureTowerFeature"].GetInt32()==17,"Tower state, weekly inventory and unknown fields survive restart and native game-info query");}
        File.WriteAllText(Path.Combine(root,"evidence/upgrade-tower-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,checkCount=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Upgrade materials and challenge tower: {checks.Count}/{checks.Count}");
    }
}
