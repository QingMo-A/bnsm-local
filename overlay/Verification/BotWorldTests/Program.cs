extern alias game;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

string root="I:/BnsmEssenceBuildWork";
if(args.Contains("--bot-numeric-prepare")){await BotNumericStartupRegression.Prepare("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--bot-numeric-test")){await BotNumericStartupRegression.Verify("I:/BnsmModernWork/WindowsPreview");return;}
#if BNSM_EMBEDDED_TESTS
if(args.Contains("--embedded-save-test")){await EmbeddedSaveRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
#endif
if(args.Contains("--helper-costume-test")){await HelperCostumeRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--bot-management-test")){BotManagementRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--world-war-test")){await WorldWarLifecycleRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--gacha-test")){await GachaTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--chain-test")){await ChainBattleRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--chain-fixture")){await ChainBattleRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--chain-fixture")+1]);return;}
if(args.Contains("--chain-audit")){ChainBattleRegression.Audit("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--story-test")){await StoryChapterRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--story-fixture")){await StoryChapterRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--story-fixture")+1]);return;}
if(args.Contains("--original-card-test")){OriginalCardRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--original-card-fixture")){await OriginalCardRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--original-card-fixture")+1]);return;}
if(args.Contains("--infinite-defence-fixture")){await InfiniteDefenceFixture.Run("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--infinite-defence-fixture")+1]);return;}
if(args.Contains("--guild-battle-test")){await GuildBattleRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--guild-battle-fixture")){await GuildBattleRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--guild-battle-fixture")+1]);return;}
if(args.Contains("--world-raid-test")){await WorldRaidSettlementRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--world-raid-fixture")){await WorldRaidSettlementRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--world-raid-fixture")+1]);return;}
if(args.Contains("--rank-deck-test")){await RankDeckRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-invasion-test")){await CastleInvasionRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-tower-test")){await CastleTowerRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--upgrade-tower-regression")){await UpgradeTowerTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-tower-fixture")){await CastleTowerRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--castle-tower-fixture")+1]);return;}
if(args.Contains("--activity-catalog-audit")){ActivityExpansionRegression.Audit("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--activity-expansion-test")){await ActivityExpansionRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--activity-regression")){await ActivityTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--activity-expansion-fixture")){await ActivityExpansionRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--activity-expansion-fixture")+1]);return;}
if(args.Contains("--manual-pvp-test")){await ManualPvpRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--manual-pvp-replay-audit")){ManualPvpRegression.AuditReplay("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--manual-pvp-replay-audit")+1]);return;}
if(args.Contains("--manual-pvp-fixture")){await ManualPvpRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--manual-pvp-fixture")+1]);return;}
if(args.Contains("--manual-pvp-audit")){
 using var f=File.OpenRead("I:/BnsmModernWork/WindowsPreview/LocalServer/data/tables/BattleLogicConfig.pb");var rows=ProtoBuf.Serializer.Deserialize<BscTable.BattleLogicConfigTable>(f).List;
 foreach(var row in rows.Where(x=>(int)x.GameMode is 1 or 9 or 10 or 12 or 42))Console.WriteLine(JsonSerializer.Serialize(row,new JsonSerializerOptions{IncludeFields=true}));return;
}
if(args.Contains("--sweep-material-audit")){SweepMaterialRegression.Audit("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--sweep-material-test")){await SweepMaterialRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--adventure-drop-test")){await AdventureDropRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-monster-test")){await CastleRaidRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-monster-fixture")){await CastleRaidRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--castle-monster-fixture")+1]);return;}
if(args.Contains("--friend-raid-audit")){
 var catalog=new CompetitiveCatalog("I:/BnsmModernWork/WindowsPreview");
 using var file=File.OpenRead("I:/BnsmModernWork/WindowsPreview/LocalServer/data/tables/FriendRaidBossDataTable.pb");
 var cards=ProtoBuf.Serializer.Deserialize<BscTable.CardDataTable>(file).List;
 Console.WriteLine(JsonSerializer.Serialize(new{maps=catalog.CastleMaps.Select(m=>new{m.MapId,spots=m.SpotList.Where(s=>catalog.Castles.Any(c=>c.Id==s.CastleId&&c.BossId>0)).Select(s=>new{s.SpotId,s.CastleId,s.RequirePcLevel,s.LinkSpot})}),castles=catalog.Castles.Where(c=>c.BossId>0).Select(c=>new{c.Id,c.BossId,c.SummonCost,c.NormalRewardIdType,c.NormalRewardId,c.NormalRewardQuantity,c.PerfectRewardIdType,c.PerfectRewardId,c.PerfectRewardQuantity,c.FinalRewardIdType,c.FinalRewardId,c.FinalRewardQuantity}),cards=cards.Select(c=>new{c.Id,c.Level,c.StatHpFriendBattle,c.StatHpFinalBattle,c.DamageMin,c.DamageMax,c.MaxSp})},new JsonSerializerOptions{IncludeFields=true}));return;
}
if(args.Contains("--castle-explore-instant-test")){await CastleExploreInstantRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-explore-instant-fixture")){await CastleExploreInstantRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--castle-explore-instant-fixture")+1]);return;}
if(args.Contains("--guild-bots-test")){await GuildBotRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--guild-bots-fixture")){await GuildBotRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--guild-bots-fixture")+1]);return;}
if(args.Contains("--guild-regression")){await GuildTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--guild-classic-fixture")){await GuildTest.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--guild-classic-fixture")+1]);return;}
if(args.Contains("--pvp-difficulty-test")){await PvpDifficultyRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--castle-lifecycle-fixture")){await CastleLifecycleRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--castle-lifecycle-fixture")+1]);return;}
if(args.Contains("--castle-lifecycle-test")){await CastleLifecycleRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--loot-regression")){await LootTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--shop-regression")){await ShopTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--attendance-regression")){await AttendanceTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--document-test")){await DocumentRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--document-fixture")){await DocumentRegression.Fixture("I:/BnsmModernWork/WindowsPreview",args[Array.IndexOf(args,"--document-fixture")+1]);return;}
if(args.Contains("--document-audit")){
    var c=new CompetitiveCatalog("I:/BnsmModernWork/WindowsPreview");var catalog=new GameCatalog("I:/BnsmModernWork/WindowsPreview");
    var rows=c.Rewards.Where(x=>(int)x.BattleResultType==4 && new[]{2,3,9,23}.Contains((int)x.PvpBattleType)).Select(x=>new{x.Id,mode=(int)x.PvpBattleType,x.Ranking_Min,x.Ranking_Max,x.ItemQuantity,x.RewardValueIdType,x.RewardValueId}).ToList();
    var shops=catalog.Shops.Where(x=>x.ShopId==4);var goods=catalog.Goods.Where(x=>x.ArenaP>0).Select(x=>new{x.GoodsId,x.ProductId,x.ProductIdType,x.ArenaP,x.PcLevelMin,x.PcLevelMax,x.Quantity});
    File.WriteAllText("I:/BnsmModernWork/Verification/document-server-audit.json",JsonSerializer.Serialize(new{rows,shops,goods},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return;
}
if(args.Contains("--battle-lifecycle-test")){await BattleLifecycleRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--battle-lifecycle-fixture")){await BattleLifecycleRegression.Fixture(root,args[Array.IndexOf(args,"--battle-lifecycle-fixture")+1]);return;}
if(args.Contains("--competitive-audit")){CompetitiveRegression.Audit("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--competitive-test")){await CompetitiveRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--competitive-fixture")){await CompetitiveRegression.Fixture(root,args[Array.IndexOf(args,"--competitive-fixture")+1]);return;}
if(args.Contains("--skill-enchant-audit")){SkillEnchantRegression.Audit("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--skill-enchant-test")){await SkillEnchantRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--skill-enchant-fixture")){await SkillEnchantRegression.Fixture(root,args[Array.IndexOf(args,"--skill-enchant-fixture")+1]);return;}
if(args.Contains("--big-boss-entry-test")){await BigBossEntryRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--big-boss-audit")){
    var raids=new RaidCatalog(root);
    File.WriteAllText("I:/BnsmModernWork/Verification/big-boss-audit.json",JsonSerializer.Serialize(raids.Big.Select(x=>new{x.Id,x.NameAlias,x.__Name,x.BossGrade,x.Level,x.BigBossStageId,x.BigBossRewardId,stage=raids.BigStages.FirstOrDefault(s=>s.Id==x.BigBossStageId)}),new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));return;
}
if(args.Contains("--resource-missions-test")){await ResourceMissionRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--stage-preview-fixture")){await FactionRegression.Fixture(root,args[Array.IndexOf(args,"--stage-preview-fixture")+1],desktopPages:true,stagePreview:true);return;}
if(args.Contains("--resource-event-test")){await ResourceEventRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--stamina-friend-test")){await StaminaFriendRegression.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--desktop-pages-fixture")){await FactionRegression.Fixture(root,args[Array.IndexOf(args,"--desktop-pages-fixture")+1],desktopPages:true);return;}
if(args.Contains("--faction-test")){await FactionRegression.Run(root);return;}
if(args.Contains("--faction-fixture")){await FactionRegression.Fixture(root,args[Array.IndexOf(args,"--faction-fixture")+1]);return;}
if(args.Contains("--phone-support")){await PhoneSupportRegression.Run(root);return;}
if(args.Contains("--wander-entry")){await WanderEntryRegression.Run(root);return;}
if(args.Contains("--social-regression")){await FriendsTest.Run("I:/BnsmModernWork/WindowsPreview");await WorldChatTest.Run("I:/BnsmModernWork/WindowsPreview");return;}
if(args.Contains("--mission-regression")){await MissionTest.Run(root);return;}
if(args.Contains("--mission-reward-audit")){MissionRewardRegression.Audit(root);return;}
if(args.Contains("--mission-reward-regression")){await MissionRewardRegression.Run(root);return;}
if(args.Contains("--mission-reported-families")){await MissionRewardRegression.ReportedFamilies(root);return;}
string fixture=Path.Combine("I:/BnsmModernWork/Verification","bot-world-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);var checks=new List<string>();
void Check(bool value,string message){if(!value)throw new InvalidDataException(message);checks.Add(message);Console.WriteLine("PASS "+message);}
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
var timer=Stopwatch.StartNew();
await using(var runtime=new LocalRuntime(root,Path.Combine(fixture,"saves"),0,0,0,false))
{
    long startup=timer.ElapsedMilliseconds;
    var world=runtime.Router.Bots;var bots=world.All();var validator=new LocalBotBuilder(runtime.Catalog);
    Check(bots.Count==12,"Default population is twelve persistent residents");
    Check(bots.Select(x=>x.Strength).Distinct().Count()==4,"Mixed population contains all four strength tiers");
    Check(bots.Select(x=>x.Style).Distinct().Count()==5,"All five build archetypes are represented");
    Check(bots.Select(x=>x.Uid).Distinct().Count()==bots.Count,"Bot identities are unique");
    foreach(var bot in bots)validator.Validate(bot);
    Check(bots.All(b=>b.Profile.Deck.Count==5&&b.Profile.EquipmentItems.Count==15),"Every bot has five real heroes and fifteen distinct legal equipment instances");
    Check(bots.All(b=>b.Profile.CardStates.Values.All(s=>s.GemIds.All(x=>x!=0))),"Every card has three real catalog gems");
    var human=runtime.Store.GetOrCreate("bot-world-test-human");
    Check(human.Uid==1001&&runtime.Store.All().Count==1,"Bots are separate from human accounts and UID allocation");
    string playerFile=Path.Combine(fixture,"saves/players.json"),worldFile=Path.Combine(fixture,"saves/bot-world.json");
    string humanBefore=Hash(playerFile),worldBefore=Hash(worldFile);
    var reloaded=new LocalBotWorld(root,Path.Combine(fixture,"saves"),runtime.Catalog,runtime.Store.All());
    Check(Hash(worldFile)==worldBefore&&JsonSerializer.Serialize(reloaded.All())==JsonSerializer.Serialize(bots),"Restart preserves identity, seed, tier, deck and gear without rewriting roster");
    world.Add(3,"expert",runtime.Store.All());
    Check(world.All().Count==15&&world.All().TakeLast(3).All(x=>x.Strength=="expert"),"Runtime generation adds the requested population and tier");
    Check(Hash(playerFile)==humanBefore,"Bot generation does not rewrite human saves");
    var selected=world.All()[0];uint id=selected.Uid;string name=selected.Profile.Nickname;
    world.Configure(id,"casual","control","legend");var changed=world.Find(id)!;
    Check(changed.Profile.Nickname==name&&changed.Revision==2,"Build changes preserve identity and increment revision");
    Check(changed.Strength=="casual"&&changed.AiStrength=="legend"&&changed.Tactics.ReactionMilliseconds==400&&changed.Tactics.ChainControl,"Gear strength and future AI policy can be configured independently");
    string gear=JsonSerializer.Serialize(changed.Profile);world.Configure(id,"casual","control","legend");
    Check(JsonSerializer.Serialize(world.Find(id)!.Profile)==gear,"Saved seed reproduces the same build");
    string prior=Hash(worldFile);bool invalid=false;
    try{world.Add(100,"mixed",runtime.Store.All());}catch(ArgumentException){invalid=true;}
    Check(invalid&&Hash(worldFile)==prior&&world.All().Count==15,"Population limit rejects atomically");
    invalid=false;try{world.Configure(id,"impossible","control");}catch(ArgumentException){invalid=true;}
    Check(invalid&&Hash(worldFile)==prior,"Unknown templates preserve the saved world");
    var debug=new LocalDebugService(runtime);
    using(var r=JsonDocument.Parse(JsonSerializer.Serialize(debug.Handle("GET","/debug/bots",null,null))))
        Check(r.RootElement.GetProperty("bots").GetArrayLength()==15&&!r.RootElement.GetProperty("pvpReady").GetBoolean(),"API exposes the roster and accurately reports pending PvP execution");
    using(var r=JsonDocument.Parse(JsonSerializer.Serialize(debug.Handle("POST","/debug/bots/generate",null,"{\"count\":0}"))))
        Check(!r.RootElement.GetProperty("ok").GetBoolean(),"API rejects invalid generation requests");
    using(var peer=new Peer(root))
    {
        await peer.Login(runtime,"bot-world-test-human");
        var recommend=await peer.Request<GsRecommendFriendOk>(GameServerCommand.GS_RECOMMEND_FRIEND_REQUEST,new GsRecommendFriendRequest());
        Check(recommend.recommendList.Any(x=>x.uid==id)&&recommend.recommendList.Count==15,"Original encrypted friend recommendations expose generated residents");
        var profile=await peer.Request<GsGetAnotherUserGameInfoOk>(GameServerCommand.GS_GET_ANOTHER_USER_GAME_INFO_REQUEST,new GsGetAnotherUserGameInfoRequest{targetUid=id});
        Check(profile.nameCardInfo.introMsg.Contains("人机")&&profile.townExhibitionCardInfoList.Count==5&&profile.townExhibitionDeckInfo.cardSlot5>0,"Native profile identifies the bot and displays all five cards");
        var detail=await peer.Request<GsGetRankingBoardArenaBattleDetailInfoOk>(GameServerCommand.GS_GET_RAINKING_BOARD_ARENA_BATTLE_DETAIL_REQUEST,new GsGetRankingBoardArenaBattleDetailInfoRequest{targetUid=id,rankingBoardType=BscTable.RANKING_BOARD_TYPE.RANKING_BOARD_ARENA_BATTLE_REAL_TIME});
        Check(detail.battleCardInfoList.Count==5&&detail.battleCardInfoList.All(x=>x.cardEquipInfo.Count==6&&x.cardSkillLevel.Count==7),"PvP preview carries all gear/gem slots and skill levels");
        await peer.Request<object>(GameServerCommand.GS_INVITE_FRIEND_REQUEST,new GsInviteFriendRequest{inviteUid=id});
        Check(human.Friends.Links.ContainsKey(id),"Generated residents support native add-friend interaction");
    }
    double AverageStep(string tier)=>world.All().Where(x=>x.Strength==tier).SelectMany(x=>x.Profile.EquipmentItems).Select(x=>runtime.Catalog.ItemsById[(int)x.ItemId].Step).Average();
    double casual=AverageStep("casual"),legend=AverageStep("legend");
    Check(legend>casual,"Legend equipment has higher native progression than casual equipment");
    int generatedBuilds=0;
    foreach(string tier in LocalBotWorld.Strengths)
    foreach(string style in LocalBotWorld.Styles)
    foreach(int seed in new[]{7,101,20261031})
    {
        var sample=validator.Build(999001,"模板检查",tier,style,seed,ProtocolRouter.Now);
        validator.Validate(sample);generatedBuilds++;
        CheckBuildCaps(sample,runtime.Catalog);
    }
    Check(generatedBuilds==60,"Sixty seeded builds across all tiers and styles obey native skill and enchant limits");
    var workspace=new SaveWorkspace(Path.Combine(fixture,"backup-workspace"));
    var listed=JsonSerializer.SerializeToElement(workspace.Command("list",legacy:Path.Combine(fixture,"saves")));
    string active=listed.GetProperty("activeProfileId").GetString()!;
    string archive=Path.Combine(fixture,"bot-world.bnsm-save.zip");
    workspace.Command("export",id:active,file:archive);
    var imported=JsonSerializer.SerializeToElement(workspace.Command("import",name:"Bot round trip",file:archive));
    string restored=imported.GetProperty("profile").GetProperty("Id").GetString()!;
    Check(Hash(Path.Combine(workspace.Saves(restored),"bot-world.json"))==Hash(worldFile),"Profile backup export/import preserves the complete bot world byte for byte");
    File.WriteAllText(Path.Combine(fixture,"build-examples.json"),JsonSerializer.Serialize(world.All().Where(x=>x.Strength is "legend" or "expert").Take(4),new JsonSerializerOptions{WriteIndented=true}));
    BuildExampleExporter.Write(root,runtime.Catalog,world.All(),"I:/BnsmModernWork/Verification/bot-build-examples.json");
    File.WriteAllText("I:/BnsmModernWork/Verification/bot-world-tests.json",JsonSerializer.Serialize(new{passed=true,checks,startupMilliseconds=startup,fixture,botCount=world.All().Count,casualEquipmentStep=casual,legendEquipmentStep=legend,originalPlayerSaveUsed=false,pvpBattleExecutionTested=false},new JsonSerializerOptions{WriteIndented=true}));
}
Console.WriteLine("BOT_WORLD_TESTS_PASSED "+checks.Count);

static void CheckBuildCaps(LocalBot bot,GameCatalog catalog)
{
    int[] process=[0,4,1,2,3,5,6];
    foreach(var seq in bot.Profile.Deck)
    {
        var s=bot.Profile.CardStates[seq];var card=catalog.Cards.Single(c=>c.Id==bot.Profile.CardIds[(int)seq-1]);
        var skills=s.ActiveSkills.Concat(s.PassiveSkills).ToArray();
        for(int slot=0;slot<skills.Length;slot++)
        {
            int cap=Math.Min((int)s.Level,Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_SKILL_LEVEL],catalog.CardProcesses.GetSkillItem(process[slot])?.ProcessDataList.Count??1));
            if(skills[slot]<1||skills[slot]>Math.Max(1,cap))throw new InvalidDataException("Native skill cap exceeded");
        }
        int capEnchant=Math.Min(catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_ENCHANT),catalog.CardEnchants.FirstOrDefault(x=>x.EnchantType==card.EnchantType)?.MaterialDataList.TakeWhile(x=>x.MinLevel<=s.Level).Count()??0);
        if(s.Enchant>capEnchant)throw new InvalidDataException("Native enchant cap exceeded");
    }
}

sealed class Peer(string root):IDisposable
{
    public byte[] LastPayload {get;private set;}=[];
    private readonly TcpClient gameSocket=new();
    private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
    private SessionKeyInfo key=new();
    private async Task<(ushort Command,byte[] Payload)> Ask(TcpClient client,ushort command,object value)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(value),true),timeout.Token);
        var decoded=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);
        return(decoded.Command,decoded.Payload);
    }
    public async Task Login(LocalRuntime server,string account)
    {
        using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",server.LoginPort);
        await Ask(socket,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
        var r=await Ask(socket,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true});
        var started=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),r.Payload)!;key=new(){uid=started.uid,loginSessionKey=started.loginSessionKey};
        await gameSocket.ConnectAsync("127.0.0.1",server.GamePort);
    }
    public async Task<T> Request<T>(GameServerCommand command,object request,bool retry=false,bool success=true)
    {
        if(!retry)key.sequenceNo++;(request.GetType().GetField("keyInfo")??request.GetType().GetField("KeyInfo"))?.SetValue(request,key);
        var r=await Ask(gameSocket,(ushort)command,request);
        LastPayload=r.Payload;
        if(((GameServerCommand)r.Command).ToString()!=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL"))throw new Exception("Unexpected reply: "+(GameServerCommand)r.Command+" "+System.Text.Encoding.UTF8.GetString(r.Payload));
        if(success && typeof(T)==typeof(GsGetDebugPromotionInfoOk)) {
            var merged=(GsGetDebugPromotionInfoOk)WireCodec.Deserialize(typeof(GsGetDebugPromotionInfoOk),r.Payload)!;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(merged.currentCount<merged.totalCount) {
                var frame=codec.Decode((await WireCodec.ReadFrame(gameSocket.GetStream(),timeout.Token))!,true);
                if(frame.Command!=r.Command)throw new InvalidDataException("Unexpected multipart activity command");
                var next=(GsGetDebugPromotionInfoOk)WireCodec.Deserialize(typeof(GsGetDebugPromotionInfoOk),frame.Payload)!;
                if(next.currentCount==0||next.totalCount!=merged.totalCount)throw new InvalidDataException("Invalid activity part");
                merged.currentCount+=next.currentCount;merged.promotionDataList.AddRange(next.promotionDataList);
            }
            return (T)(object)merged;
        }
        if(success && typeof(T)==typeof(GsGetRewardInfoOk)) {
            var merged=(GsGetRewardInfoOk)WireCodec.Deserialize(typeof(GsGetRewardInfoOk),r.Payload)!;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(merged.dataCount<merged.totalDataCount){
                var frame=codec.Decode((await WireCodec.ReadFrame(gameSocket.GetStream(),timeout.Token))!,true);
                if(frame.Command!=r.Command)throw new InvalidDataException("Unexpected multipart mail command");
                var next=(GsGetRewardInfoOk)WireCodec.Deserialize(typeof(GsGetRewardInfoOk),frame.Payload)!;
                if(next.dataCount==0||next.totalDataCount!=merged.totalDataCount)throw new InvalidDataException("Invalid mail part");
                merged.dataCount+=next.dataCount;merged.rewardList.AddRange(next.rewardList);
            }
            return (T)(object)merged;
        }
        return typeof(T)==typeof(object)?(T)new object():(T)WireCodec.Deserialize(typeof(T),r.Payload)!;
    }
    public void Dispose()=>gameSocket.Dispose();
}

internal static class DebugToolsTest
{
    public static async Task<WebApplication> Host(LocalRuntime runtime)
    {
        var builder=WebApplication.CreateSlimBuilder();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        var app=builder.Build();DebugApi.Map(app,runtime);await app.StartAsync();return app;
    }
}
