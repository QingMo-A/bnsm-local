extern alias game;
using ProtoBuf;
using System.Text.Json;

if (args.Contains("--extract-tables"))
{
    TableExtractor.Extract();
    return;
}

if (args.Contains("--codec-check"))
{
    var value = new DsGetServerListRequest { openId = "local-test" };
    using var stream = new MemoryStream();
    Serializer.Serialize(stream, value);
    stream.Position = 0;
    var decoded = Serializer.Deserialize<DsGetServerListRequest>(stream);
    if (decoded.openId != value.openId) throw new Exception("Protocol codec check failed");
    System.Console.WriteLine("Original game message codecs work in the local server runtime.");
    return;
}
var root = TableExtractor.FindRoot();
if(args.Contains("--essence-shop-test")){await EssenceShopTest.Run(root);return;}
if(args.Contains("--essence-shop-fixture")){await EssenceShopTest.Fixture(root,args[Array.IndexOf(args,"--essence-shop-fixture")+1]);return;}
if(args.Contains("--activity-test")){await ActivityTest.Run(root);return;}
if(args.Contains("--activity-fixture")){await ActivityTest.Fixture(root,args[Array.IndexOf(args,"--activity-fixture")+1]);return;}
if(args.Contains("--board-resource-test")){await BoardFactionTest.Run(root);return;}
if(args.Contains("--board-resource-fixture")){await BoardFactionTest.Fixture(root,args[Array.IndexOf(args,"--board-resource-fixture")+1]);return;}
if(args.Contains("--board-faction-audit")){BoardFactionAudit.Run(root);return;}
if(args.Contains("--mission-test")){await MissionTest.Run(root);return;}
if(args.Contains("--mission-fixture")){await MissionTest.Fixture(root,args[Array.IndexOf(args,"--mission-fixture")+1]);return;}
if(args.Contains("--mission-audit")){MissionAudit.Run(root);return;}
if(args.Contains("--wander-viewport-fixture")){await WanderViewportTest.Fixture(root,args[Array.IndexOf(args,"--wander-viewport-fixture")+1]);return;}
if(args.Contains("--battle-exit-test")){await BattleExitTest.Run(root);return;}
if(args.Contains("--battle-exit-fixture")){await BattleExitTest.Fixture(root,args[Array.IndexOf(args,"--battle-exit-fixture")+1]);return;}
if(args.Contains("--raid-test")) {await RaidTest.Run(root);return;}
if(args.Contains("--raid-player-fixture")) {await RaidTest.Fixture(root,args[Array.IndexOf(args,"--raid-player-fixture")+1]);return;}
if(args.Contains("--performance-fixture")) {await PerformanceTest.Fixture(root,args[Array.IndexOf(args,"--performance-fixture")+1]);return;}
if(args.Contains("--performance-test")) {int b=Array.IndexOf(args,"--compare-server");await PerformanceTest.Run(root,b<0?null:args[b+1]);return;}
if(args.Contains("--world-chat-test")) {await WorldChatTest.Run(root);return;}
if(args.Contains("--world-chat-fixture")) {await WorldChatTest.Fixture(root,args[Array.IndexOf(args,"--world-chat-fixture")+1]);return;}
if(args.Contains("--ranking-test")) {await RankingTest.Run(root);return;}
if(args.Contains("--ranking-player-fixture")) {await RankingTest.Fixture(root,args[Array.IndexOf(args,"--ranking-player-fixture")+1]);return;}
if(args.Contains("--ranking-audit")) {RankingAudit.Run(root);return;}
if(args.Contains("--friends-test")) {await FriendsTest.Run(root);return;}
if(args.Contains("--friends-player-fixture")) {await FriendsTest.Fixture(root,args[Array.IndexOf(args,"--friends-player-fixture")+1]);return;}
if(args.Contains("--friends-audit")) {FriendsAudit.Run(root);return;}
if(args.Contains("--guild-test")) {await GuildTest.Run(root);return;}
if(args.Contains("--guild-player-fixture")) {await GuildTest.Fixture(root,args[Array.IndexOf(args,"--guild-player-fixture")+1]);return;}
if(args.Contains("--guild-audit")) {GuildAudit.Run(root);return;}
if(args.Contains("--raid-audit")) {RaidAudit.Run(root);return;}
if(args.Contains("--batch-equipment-test")) {await BatchEquipmentTest.Run(root);return;}
if(args.Contains("--batch-equipment-player-fixture")) {await BatchEquipmentTest.Fixture(root,args[Array.IndexOf(args,"--batch-equipment-player-fixture")+1]);return;}
if(args.Contains("--polish-audit")) {PolishAudit.Run(root);return;}
if(args.Contains("--debug-equipment-test")) {await DebugEquipmentTest.Run(root);return;}
if(args.Contains("--debug-equipment-fixture")) {await DebugEquipmentTest.Fixture(root,args[Array.IndexOf(args,"--debug-equipment-fixture")+1]);return;}
if(args.Contains("--debug-rules-test")) {await DebugRulesTest.Run(root);return;}
if(args.Contains("--debug-rules-player-fixture")) {await DebugRulesTest.Fixture(root,args[Array.IndexOf(args,"--debug-rules-player-fixture")+1]);return;}
if(args.Contains("--polish-test")) {await PolishTest.Run(root);return;}
if(args.Contains("--polish-player-fixture")) {await PolishTest.Fixture(root,args[Array.IndexOf(args,"--polish-player-fixture")+1]);return;}
if(args.Contains("--transcendence-audit")) {TranscendenceAudit.Run(root);return;}
if(args.Contains("--transcendence-test")) {await TranscendenceTest.Run(root);return;}
if(args.Contains("--transcendence-player-fixture")) {await TranscendenceTest.Fixture(root,args[Array.IndexOf(args,"--transcendence-player-fixture")+1]);return;}
if(args.Contains("--embedded-host-test")) {await EmbeddedHostTest.Run(root);return;}
if(args.Contains("--skill-lifecycle-test")) {await SkillLifecycleTest.Run(root);return;}
if(args.Contains("--skill-lifecycle-player-fixture")) {await SkillLifecycleTest.Fixture(root,args[Array.IndexOf(args,"--skill-lifecycle-player-fixture")+1]);return;}
if(args.Contains("--card-availability-test")) {await CardAvailabilityTest.Run(root);return;}
if(args.Contains("--card-availability-player-fixture")) {await CardAvailabilityTest.Fixture(root,args[Array.IndexOf(args,"--card-availability-player-fixture")+1]);return;}
if(args.Contains("--card-availability-audit")) {CardAvailabilityAudit.Run(root);return;}
if(args.Contains("--upgrade-tower-test")) {await UpgradeTowerTest.Run(root);return;}
if(args.Contains("--upgrade-tower-player-fixture")) {await UpgradeTowerTest.Fixture(root,args[Array.IndexOf(args,"--upgrade-tower-player-fixture")+1]);return;}
if(args.Contains("--upgrade-tower-catalog")) {UpgradeTowerCatalogAudit.Run(root);return;}
if(args.Contains("--engraving-test")) {await EngravingTest.Run(root);return;}
if(args.Contains("--engraving-player-fixture")) {await EngravingTest.Fixture(root,args[Array.IndexOf(args,"--engraving-player-fixture")+1]);return;}
if(args.Contains("--engraving-catalog")) {EngravingCatalogAudit.Run(root);return;}
if(args.Contains("--profile-test")) {await ProfileTest.Run(root);return;}
if(args.Contains("--profile-player-fixture")) {await ProfileTest.Fixture(root,args[Array.IndexOf(args,"--profile-player-fixture")+1]);return;}
if(args.Contains("--profile-catalog")) {ProfileCatalogAudit.Run(root);return;}
if(args.Contains("--evil-war-test")) {await EvilWarTest.Run(root);return;}
if(args.Contains("--evil-war-player-fixture")) {await EvilWarTest.Fixture(root,args[Array.IndexOf(args,"--evil-war-player-fixture")+1],args.Contains("--evil-war-early-fixture"));return;}
if(args.Contains("--inn-test")) {await InnTest.Run(root);return;}
if(args.Contains("--inn-player-fixture")) {await InnTest.Fixture(root,args[Array.IndexOf(args,"--inn-player-fixture")+1]);return;}
if(args.Contains("--inn-catalog")) {InnCatalogAudit.Run(root);return;}
if(args.Contains("--evil-war-catalog")) {EvilWarCatalogAudit.Run(root);return;}
if(args.Contains("--training-test")) {await TrainingTest.Run(root);return;}
if(args.Contains("--training-player-fixture")) {await TrainingTest.Fixture(root,args[Array.IndexOf(args,"--training-player-fixture")+1]);return;}
if(args.Contains("--training-catalog")) {TrainingCatalogAudit.Run(root);return;}
if(args.Contains("--debug-test")) {await DebugToolsTest.Run(root);return;}
if(args.Contains("--debug-player-fixture")) {await DebugToolsTest.Fixture(root,args[Array.IndexOf(args,"--debug-player-fixture")+1]);return;}
if(args.Contains("--debug-catalog")) {DebugCatalogAudit.Run(root);return;}
if(args.Contains("--helper-test")) {await HelperTest.Run(root);return;}
if(args.Contains("--art-helper-catalog")) {ArtHelperCatalogAudit.Run(root);return;}
if(args.Contains("--market-catalog")) {MarketCatalogAudit.Run(root);return;}
if(args.Contains("--loot-catalog")) {LootCatalogAudit.Run(root);return;}
if(args.Contains("--loot-test")) {await LootTest.Run(root);return;}
if(args.Contains("--crafting-catalog")) {CraftingCatalogAudit.Run(root);return;}
if(args.Contains("--crafting-test")) {await CraftingTest.Run(root);return;}
if(args.Contains("--adventure-catalog")) {AdventureCatalogAudit.Run(root);return;}
if(args.Contains("--adventure-test")) {await AdventureTest.Run(root);return;}
if(SaveWorkspace.TryCommand(args,Path.Combine(root,"LocalServer/saves")))return;
if(args.Contains("--save-workspace-test")) {SaveWorkspaceTest.Run(root);return;}
if(args.Contains("--export-gacha-presentation")) {
    var rules=GachaRules.Load(root);
    File.WriteAllText(Path.Combine(root,"UnityRebuilt/ExportedProject/Assets/StreamingAssets/LocalGachaRules.json"),JsonSerializer.Serialize(rules,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));return;
}
if(args.Contains("--export-full-shop")) {new FullShopCatalog(new GameCatalog(root)).Export(root);return;}
if(args.Contains("--commerce-fixture")) {
    int index=Array.IndexOf(args,"--commerce-fixture");string output=Path.GetFullPath(args[index+1]);
    if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use the dedicated verification folder");
    var c=new GameCatalog(root);var p=new LocalPlayer {Uid=1001,Account="local-player",Level=3,Exp=(uint)c.PlayerStates.Single(x=>x.Level==3).Exp,CardIds=c.StarterCards(),SkillPoint=10,SkillPointStamp=ProtocolRouter.Now};
    p.Deck=[1,2,3,4,5];Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"players.json"),JsonSerializer.Serialize(new[] {p},new JsonSerializerOptions {WriteIndented=true}));return;
}
if(args.Contains("--commerce-test")) {await CommerceTest.Run(root);return;}
if(args.Contains("--attendance-test")) {await AttendanceTest.Run(root);return;}
if(args.Contains("--equipment-test")) {await EquipmentTest.Run(root);return;}
if(args.Contains("--shop-test")) {await ShopTest.Run(root);return;}
if(args.Contains("--gacha-test")) {await GachaTest.Run(root);return;}
if(args.Contains("--gacha-catalog")) {
    var c=new GameCatalog(root);
    var usable=c.UsableHeroes.ToList();
    var byRank=usable.GroupBy(x=>x.Rank).OrderBy(g=>g.Key).Select(g=>new {rank=g.Key,count=g.Count()}).ToList();
    var four=usable.Where(x=>x.Rank==4).ToList();
    var report=new {
        gachaSets=c.GachaSets.Select(s=>new {s.GachaCategoryIndex,s.GachaCount,s.CostItemIdType,s.CostItemId,s.CostItemQuantity,
            s.FreeActivate,s.FreeCountMax,s.RegenTime,s.SpecialDropProb,
            bonus=new {type=s.BonusRewardIdType,id=s.BonusRewardId,qty=s.BonusRewardQuantity},
            segments=s.GachaSetDataInfoList.Select(i=>new {i.PcLvMin,i.PcLvMax,i.IsFirst,slots=i.Slot.Count,
                vipLevels=i.VipLevel.Count,special=i.SpecialDrop.Count})}).ToList(),
        usableCount=usable.Count,
        rankHistogram=byRank,
        rank4Count=four.Count,
        rank4Sample=four.Take(12).Select(x=>new {x.Id,x.Rank,x.MaxRank,x.Talent,x.CollectionId,x.PieceId,x.PieceQuantity,x.EvolveFirstAncestor,x.Character}),
        rank4Talent=four.GroupBy(x=>x.Talent).Select(g=>new {talent=g.Key.ToString(),count=g.Count()}),
        rank4Piece=four.GroupBy(x=>x.PieceId==0).Select(g=>new {zeroPiece=g.Key,count=g.Count()}),
        rank4PieceQty=four.GroupBy(x=>x.PieceQuantity).OrderBy(g=>g.Key).Take(10).Select(g=>new {qty=g.Key,count=g.Count()}),
        currentDrawPool=c.DrawPool(false,GachaRules.Load(root).CardRank).Select(x=>new {x.Id,x.Rank,x.Talent}).Take(10),
        currentDrawPoolRanks=c.DrawPool(false,GachaRules.Load(root).CardRank).GroupBy(x=>x.Rank).Select(g=>new {rank=g.Key,count=g.Count()}),
        cashDrawPoolRanks=c.DrawPool(true,GachaRules.Load(root).CardRank).GroupBy(x=>x.Rank).Select(g=>new {rank=g.Key,count=g.Count()}),
    };
    File.WriteAllText(Path.Combine(root,"evidence/gacha-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));return;
}
if(args.Contains("--commerce-catalog")) {
    var c=new GameCatalog(root);
    File.WriteAllText(Path.Combine(root,"evidence/commerce-original-catalog.json"),JsonSerializer.Serialize(new {products=c.BuyCashProducts,cashProducts=c.CashProducts},new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));return;
}
if(args.Contains("--social-growth-fixture")) {
    int i=Array.IndexOf(args,"--social-growth-fixture");string output=Path.GetFullPath(args[i+1]);
    if(!output.StartsWith(@"D:\BnsmRecoveryWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Fixture must stay in the dedicated verification folder");
    Directory.CreateDirectory(output);var c=new GameCatalog(root);
    var players=JsonSerializer.Deserialize<List<LocalPlayer>>(File.ReadAllText(Path.Combine(root,"LocalServer/saves/players.json")))!;
    var p=players.First(x=>x.Account=="local-player");p.Level=20;p.Exp=(uint)c.PlayerStates.Single(x=>x.Level==20).Exp;p.Gold=500000;p.CashPoint=10000;
    p.ActiveStage=null;p.ActiveBattleStamp=0;p.SkillPoint=10;p.SkillPointStamp=ProtocolRouter.Now;p.SkillPointChargeCount=0;p.GrowthReceipts.Clear();
    p.CardIds[0]=20103;p.CardStates[1]=new() {Level=5,Exp=(uint)c.CardLevels.Single(x=>x.Level==5).Rank1};p.Inventory[210002001]=10000;
    var card=c.Cards.Single(x=>x.Id==20103);var materials=c.CardEnchants.Single(x=>x.EnchantType==card.EnchantType).MaterialDataList[0];
    for(int m=0;m<materials.ItemId.Count;m++)p.Inventory[(uint)materials.ItemId[m]]=materials.Count[m];
    p.Inventory[(uint)card.PieceId]=c.CardRanks.Single(x=>x.Rank==card.Rank).NextEVP;
    File.WriteAllText(Path.Combine(output,"players.json"),JsonSerializer.Serialize(players,new JsonSerializerOptions {WriteIndented=true}));return;
}
if(args.Contains("--social-growth-test")) {await SocialGrowthTest.Run(root);return;}
if(args.Contains("--growth-catalog")) {
    var c=new GameCatalog(root);
    var report=new {
        numbers=c.Numbers.Where(x=>x.Key.ToString().Contains("SKILL") || x.Key.ToString().Contains("CARD_ENCHANT") || x.Key.ToString().Contains("CARD_RANK")),
        contents=c.Contents.Where(x=>x.Alias.ToString().Contains("CARD") || x.Alias.ToString().Contains("SKILL")),
        processes=c.CardProcesses.List,
        materials=c.CardEnchants.Take(2),
        cards=c.Cards.Where(x=>x.Id is 20101 or 60023).Select(x=>new {x.Id,x.Rank,x.RankUpCardId,x.PieceId,x.EnchantType,x.ActiveSkillId,x.PassiveSkillId,x.RankUpMaterialItemId,x.RankUpMaterialItemCount}),
        ranks=c.CardRanks.Take(8),player=c.PlayerStates.Take(3),vip=c.Vip.Take(2)
        ,stages=c.Stages.Values.Where(x=>x.Id>=13 && x.Id<=20).Select(x=>new {x.Id,x.NpcRewardIdType,x.NpcRewardId,x.RewardQuantity,x.DropProb,x.FirstNpcRewardIdType,x.FirstNpcRewardId,x.FirstRewardQuantity,x.FirstDropProb})
    };
    File.WriteAllText(Path.Combine(root,"evidence/growth-original-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {IncludeFields=true,WriteIndented=true}));return;
}
if(args.Contains("--difficulty-catalog")) {
    var c=new GameCatalog(root);
    List<object> Rows(BscTable.GAME_DIFFICULTY d, Dictionary<uint,BscTable.BattleStageLink> t)=>t.Values.OrderBy(x=>x.Id)
        .Select(x=>(object)new {x.Id,x.FieldId,x.OpenStageId,x.Enterable,x.ConsumeStamina,x.Gold,x.RewardExp,x.CardRewardExp,x.DailyLimit,x.RequirePcLevel,
            waveCount=c.WaveIds(d,(uint)x.Id).Count}).ToList();
    List<object> FieldSummary(GameCatalog cat, Dictionary<uint,BscTable.BattleStageLink> t, BscTable.GAME_DIFFICULTY d)=>t.Values
        .GroupBy(x=>x.FieldId).OrderBy(g=>g.Key)
        .Select(g=>(object)new {field=g.Key,first=g.Min(x=>x.Id),last=g.Max(x=>x.Id),count=g.Count(),
            minLevel=g.Min(x=>x.RequirePcLevel),epSubNonZero=g.Count(x=>x.EpSubId!=0)}).ToList();
    var report=new {
        tutorialEndStage=GameCatalog.TutorialEndStage,
        hard=new {count=c.HardStages.Count,rows=Rows(BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD,c.HardStages)},
        legend=new {count=c.LegendStages.Count,rows=Rows(BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND,c.LegendStages)},
        missingFields=c.MissingStageFields,
        unlock=new {
            normalField=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_NORMAL),
            hardField=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_HARD),
            legendField=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_ENTRANCE_STAGE_MODE_LEGEND),
            maxPve=c.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.MAX_PVE_STAGE_ID),
            hardResetCash=c.Numbers.GetValueOrDefault(BscTable.NUMBER_TYPE.VIP_HARDSTAGE_RESET_COUNT_CASH),
            vipReset=c.Vip.Take(4).Select(x=>(object)new {x.VipLv,hard=x.HardStageMaxResetCount,legend=x.LegendStageMaxResetCount}).ToList(),
            maxStage=game::BSC_DEFINE.MAX_STAGE_ID,
            normalFieldToUnlockHard=game::BSC_DEFINE.NORMAL_FIELD_ID_TO_UNLOCK_HARD_MODE,
            hardFieldToUnlockLegend=game::BSC_DEFINE.HARD_FIELD_ID_TO_UNLOCK_LEGEND_MODE,
            normalFields=FieldSummary(c,c.Stages,BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL),
            hardFields=FieldSummary(c,c.HardStages,BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD),
            legendFields=FieldSummary(c,c.LegendStages,BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND)
        }
    };
    File.WriteAllText(Path.Combine(root,"evidence/difficulty-catalog.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
    System.Console.WriteLine("Difficulty catalog exported. hard="+c.HardStages.Count+" legend="+c.LegendStages.Count);return;
}
if(args.Contains("--difficulty-test")) {await DifficultyTest.Run(root);return;}
if(args.Contains("--stage-star-test")) {await StageStarTest.Run(root);return;}
if(args.Contains("--backfill-stage-stars")) {
    var c=new GameCatalog(root);
    var store=new PlayerStore(Path.Combine(root,"LocalServer/saves"),c);
    int n=store.BackfillMissingStageStars();
    System.Console.WriteLine($"Stage star backfill: {n} profile(s) repaired.");return;
}
if(args.Contains("--card-identity-test")) {CardIdentityPolicyTest.Run(root);return;}
if (args.Contains("--item-name-export")) {ItemNameExportAudit.Run(root);return;}
if (args.Contains("--balance-audit")) {BalanceAudit.Run(root);return;}
if (args.Contains("--combat-audit")) {CombatFormulaAudit.Run(root);return;}
if (args.Contains("--card-skill-audit")) {CardSkillAudit.Run(root);return;}
if (args.Contains("--gacha-prob-audit")) {GachaProbAudit.Run(root);return;}
if (args.Contains("--card-identity-export"))
{
    var c=new GameCatalog(root);
    var cards=c.Cards.Select(x=>new {x.Id,x.NameAlias,x.Rank,x.MaxRank,x.CollectionId,use=x.Use.ToString(),type=x.Type.ToString(),talent=x.Talent.ToString(),faction=x.Faction.ToString(),x.Level,x.EvolveFirstAncestor,x.CardMiniAtlas,x.CardMini,x.CardSmall,x.CardBig,x.Minimi,x.Character});
    var path=Path.Combine(root,"evidence/card-identities-server.json");
    File.WriteAllText(path,JsonSerializer.Serialize(cards,new JsonSerializerOptions {WriteIndented=true}));
    var profiles=JsonSerializer.Deserialize<List<LocalPlayer>>(File.ReadAllText(Path.Combine(root,"LocalServer/saves/players.json")))!;
    var plans=profiles.Select(p=>new {p.Uid,repairs=CardIdentityPolicy.Plan(c,p)});
    File.WriteAllText(Path.Combine(root,"evidence/card-identity-repair-plan.json"),JsonSerializer.Serialize(plans,new JsonSerializerOptions {WriteIndented=true}));
    System.Console.WriteLine("Original card metadata exported: "+path);return;
}
if (args.Contains("--progression-catalog"))
{
    var c = new GameCatalog(root);
    System.Console.WriteLine(JsonSerializer.Serialize(new {
        skins=c.Skins.Take(3).Select(x=>new {x.Id,x.SellPrice,x.SkinItemId,x.AncestorCardId,x.OpenVipLevel,x.Sell}),
        helpers=c.Helpers.Select(x=>new {x.HelperType,x.DefaultId,models=x.ModelList.Take(2).Select(m=>new {m.ModelId,m.ModelFile})}),
        vip=c.Vip.Take(4).Select(x=>new {x.VipLv,x.LvUpNeedVipExp,x.RecoveryStaminaBonusCount}),
        staminaTime=c.Numbers[BscTable.NUMBER_TYPE.STAMINA_RECOVERY_TIME],
        exchange=c.Exchanges.Select(x=>new {x.Id,x.ExchangeType,x.ExchangeValue,x.Discount}),
        expenses=c.Expenses.List.Where(x=>x.ExpenseType is BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_EXCHANGE_STAMINA or BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_EXCHANGE_STAMINA_LITE).Take(3),
        stages=c.Stages.Values.Take(5).Select(x=>new {x.Id,x.CardRewardExp,x.RewardExp,x.Gold}),
        levels=c.PlayerStates.Take(5).Select(x=>new {x.Level,x.Exp,x.MaxStamina,x.MaxCardLevel,x.LevelUpStaminaReward}),
        cardLevels=c.CardLevels.Take(5).Select(x=>new {x.Level,x.Rank1})
    },new JsonSerializerOptions {WriteIndented=true,IncludeFields=true})); return;
}
if (args.Contains("--catalog-summary"))
{
    var catalog = new GameCatalog(root);
    System.Console.WriteLine(JsonSerializer.Serialize(new {
        gacha = catalog.GachaSets.Select(x => new { x.GachaCategoryIndex, x.GachaCount, cost = x.CostItemIdType.ToString(), x.CostItemQuantity, x.FreeActivate, x.FreeCountMax, x.RegenTime, x.AccrueCount, x.NameAlias }),
        cash = catalog.CashProducts.Select(x => new { x.Id, x.NameAlias, x.Type, x.Cost, x.Price, x.ItemIdType, x.ItemId, x.Quantity }),
        buyCash = catalog.BuyCashProducts.Select(x => new { x.Id, x.Value, x.RewardCash, x.RewardGold }),
        heroRanks = catalog.Cards.Where(x => x.Use == BscTable.CARD_USE.USE_CARD && x.Type == BscTable.CARD_TYPE.CARD_HERO).GroupBy(x => x.Rank).Select(x => new { rank = x.Key, count = x.Count() })
    }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Contains("--feature-test"))
{
    await SupportedFeaturesTest.Run(root);
    return;
}
if (args.Contains("--progression-test"))
{
    await ProgressionTest.Run(root);
    return;
}
if (args.Contains("--self-test"))
{
    await ServerSelfTest.Run(root);
    return;
}
if(args.Any(x=>x.StartsWith("--",StringComparison.Ordinal)&&(x.EndsWith("-test",StringComparison.Ordinal)||x.EndsWith("-fixture",StringComparison.Ordinal))))
    throw new ArgumentException("Unknown diagnostic command. No save was opened.");
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:58601");
string? SaveArgument(string flag) {int i=Array.IndexOf(args,flag);return i<0?null:i+1<args.Length?args[i+1]:throw new ArgumentException("Missing "+flag);}
var workspace=new SaveWorkspace(SaveArgument("--save-root")??SaveWorkspace.DefaultRoot);
var profile=SaveArgument("--save-directory")==null ? workspace.Open(SaveArgument("--legacy-save-directory")??Path.Combine(root,"LocalServer/saves")) : default;
using var profileLease=profile.Lease;
string saveDirectory=SaveArgument("--save-directory")??profile.Directory;
await using var runtime = new LocalRuntime(root, saveDirectory);
var app = builder.Build();
app.MapGet("/health", () => Results.Json(new { status = "ready", directoryPort = runtime.DirectoryPort,
    loginPort = runtime.LoginPort, gamePort = runtime.GamePort, saveProfile=new {profile.Id,profile.Name,directory=Path.GetFullPath(saveDirectory)}, data = runtime.Router.Status() }));
// 本地调试台（开发者菜单）的后端，仅回环可达。
DebugApi.Map(app, runtime);
System.Console.WriteLine("Local server health: http://127.0.0.1:58601/health");
await app.RunAsync();
