extern alias game;
using System.Text.Json;

internal static class SkillEnchantRegression
{
    internal static void Prepare(LocalRuntime runtime)
    {
        FactionRegression.Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;
        p.CardIds=p.CardIds.Take(5).ToList();p.CardStates=p.CardStates.Where(x=>x.Key<=5).ToDictionary();
        var card=c.UsableHeroes.First(x=>x.Rank==8&&x.SkillEnchantGroupId[0]>0);
        p.CardIds[0]=(uint)card.Id;p.CardStates[1]=new(){Level=76,Exp=ProgressionRules.CardFloor(c,76),Enchant=9,ActiveSkills=[76,76],PassiveSkills=[76,76,76,76,76]};
        p.Gold=100000000;p.SkillPoint=0;p.SkillPointStamp=ProtocolRouter.Now;
        foreach(int group in card.SkillEnchantGroupId.Where(id=>id>0))foreach(var rule in c.SkillEnchants[group].SkillEnchantInfoList)
            for(int i=0;i<rule.MaterialItemId.Count;i++)p.Inventory[(uint)rule.MaterialItemId[i]]=100000;
        p.AttendanceLastDay=p.ShortTermLastDay=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=p.ShortTermDays=1;
        p.AttendanceLastTime=ProtocolRouter.Now;p.AttendanceVipClaimedDay=p.AttendanceLastDay;runtime.Store.Save();
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0)
        {
            Prepare(runtime);
            var p=runtime.Store.All().Single();
            var card=runtime.Catalog.Cards.Single(x=>x.Id==p.CardIds[0]);
            var rule=runtime.Catalog.SkillEnchants[card.SkillEnchantGroupId[0]].SkillEnchantInfoList.Single(x=>x.EnchantLevel==1);
            var pieceIndex=rule.MaterialItemId.IndexOf(card.PieceId);
            if(pieceIndex<0)throw new InvalidDataException("Native enchant fixture needs a hero fragment recipe");
            p.Inventory[(uint)card.PieceId]=rule.MaterialItemQuantity[pieceIndex]/2;
            p.Inventory[220001004]=100000;
            var hong=runtime.Catalog.Cards.Single(x=>x.EvolveFirstAncestor==LocalHongCard.FirstCardId&&x.Rank==8);
            p.CardIds[2]=(uint)hong.Id;
            p.CardStates[3]=new(){Level=76,Exp=ProgressionRules.CardFloor(runtime.Catalog,76),Enchant=9,ActiveSkills=[76,76],PassiveSkills=[76,76,76,76,76]};
            p.Inventory[(uint)hong.PieceId]=0;
            runtime.Store.Save();
        }
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
        await Task.Delay(TimeSpan.FromMinutes(20));
    }
    internal static async Task Run(string root)
    {
        string folder=Path.Combine("I:/BnsmModernWork/Verification","skill-enchant-"+Guid.NewGuid().ToString("N"));
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add(text);}
        uint finalGold;List<uint> finalEnchants;
        await using(var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            Prepare(runtime);var p=runtime.Store.All().Single();var c=runtime.Catalog;var card=c.Cards.Single(x=>x.Id==p.CardIds[0]);
            using var peer=new Peer(root);await peer.Login(runtime,p.Account);
            string State()=>JsonSerializer.Serialize(new{p.Gold,p.Inventory,p.CardStates,p.SkillPoint});
            async Task<GsSkillEnchantOk> Upgrade(int slot,bool retry=false)=>await peer.Request<GsSkillEnchantOk>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST,new GsSkillEnchantRequest{cardSeq=1,skillType=(CardSkillType)slot},retry:retry);
            async Task Reject(int slot,string label,ulong seq=1) {
                string before=State();var failed=await peer.Request<FailPacket>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST,new GsSkillEnchantRequest{cardSeq=seq,skillType=(CardSkillType)slot},success:false);
                Check(State()==before&&failed.errorCode!=(uint)ServerErrorCode.ERROR_UNKNOWN_COMMAND,label);
            }
            Check(c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SKILL_ENCHANT)==90&&c.Numbers[BscTable.NUMBER_TYPE.SKILL_ENCHANT_LIMIT_CARD_ENCHANT_GRADE]==5,"Original unlock is player 90 and card +5; card level is not a separate gate");
            var allRules=c.SkillEnchants.Values.SelectMany(x=>x.SkillEnchantInfoList).ToArray();
            Check(allRules.All(x=>x.Prob==100&&x.MoneyType==BscTable.MONEY_TYPE.MONEY_TYPE_GOLD&&x.EnchantLevel>=1&&x.EnchantLevel<=5&&c.SkillIds.Contains(x.NextEnchantSkillId)),"All 2360 original recipes resolve real skills, gold prices and 100% success without invented random failure");
            p.Level=89;await Reject(0,"Player 89 cannot bypass the unlock or spend materials");p.Level=90;
            p.CardStates[1].Enchant=4;await Reject(0,"Card +4 rejects atomically");p.CardStates[1].Enchant=5;
            uint own=p.CardIds[0];p.CardIds[0]=(uint)c.UsableHeroes.First(x=>x.Rank==7).Id;await Reject(0,"Seven-star card has no enchant path");p.CardIds[0]=own;
            await Reject(7,"Out-of-range skill slot rejects atomically");await Reject(0,"Unowned card rejects atomically",99999);
            for(int slot=0;slot<7;slot++) {
                if(slot>=card.SkillEnchantGroupId.Count||card.SkillEnchantGroupId[slot]==0){await Reject(slot,"No enchant path for slot "+slot);continue;}
                var group=c.SkillEnchants[card.SkillEnchantGroupId[slot]];
                for(int level=1;level<=5;level++) {
                    var rule=group.SkillEnchantInfoList.Single(x=>x.EnchantLevel==level);
                    if(level==1) {
                        uint gold=p.Gold;p.Gold=0;await Reject(slot,"Missing gold cannot consume materials in slot "+slot);p.Gold=gold;
                        uint material=(uint)rule.MaterialItemId[0];int count=p.Inventory[material];p.Inventory[material]=0;
                        await Reject(slot,"Missing fragments cannot consume gold in slot "+slot);p.Inventory[material]=count;
                    }
                    uint before=p.Gold;var inventory=p.Inventory.ToDictionary();uint points=p.SkillPoint??0;
                    bool substitute=slot==0&&level==2&&rule.MaterialItemId.Contains(card.PieceId);
                    const uint universal=220001004;
                    int shortage=0;
                    if(substitute)
                    {
                        uint piece=(uint)card.PieceId;
                        int required=rule.MaterialItemQuantity[rule.MaterialItemId.IndexOf(card.PieceId)];
                        p.Inventory[piece]=required/2;
                        shortage=required-p.Inventory[piece];
                        p.Inventory[universal]=shortage-1;
                        await Reject(slot,"Insufficient universal fragments leave gold and both stacks intact");
                        p.Inventory[universal]=shortage+5;
                        inventory=p.Inventory.ToDictionary();
                    }
                    var result=await Upgrade(slot);
                    Check(result.result==CraftResult.CRAFT_RESULT_SUCCESS&&result.cardInfo.cardSkillEnchant.Count==7&&result.cardInfo.cardSkillEnchant[slot]==level&&p.Gold==before-rule.Cost&&p.SkillPoint==points,"Native seven-slot response and exact gold cost at slot "+slot+" level "+level);
                    Check((substitute||result.deltaItemList.Count==rule.MaterialItemId.Count)&&result.deltaItemList.All(x=>x.itemSeq==0&&inventory[x.itemID]-p.Inventory[x.itemID]==-x.itemCount),"Exact stack deltas preserve unrelated inventory at slot "+slot+" level "+level);
                    if(substitute)Check(p.Inventory[(uint)card.PieceId]==0&&p.Inventory[universal]==5&&result.deltaItemList.Single(x=>x.itemID==universal).itemCount==-shortage,"Native enchant uses existing hero pieces and charges only the missing universal pieces");
                    string upgraded=State();await Upgrade(slot,retry:true);Check(State()==upgraded,"Network retry cannot charge twice at slot "+slot+" level "+level);
                    if(substitute){p.Inventory[(uint)card.PieceId]=100000;p.Inventory.Remove(universal);}
                }
                await Reject(slot,"Max enchant rejects without charging in slot "+slot);
            }
            Check(p.CardStates[1].Level==76&&p.CardStates[1].ActiveSkills.All(x=>x==76)&&p.CardStates[1].PassiveSkills.All(x=>x==76),"Level-76 card can enchant for a qualified player without replacing skill levels");
            var listed=await peer.Request<GsGetUserCardListOk>(GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST,new GsGetUserCardListRquest());
            Check(listed.userCardInfoList.Single(x=>x.cardSeq==1).cardSkillEnchant.SequenceEqual(p.CardStates[1].SkillEnchants),"Login card list sends persisted enchant levels");
            var preview=await peer.Request<GsGetRankingBoardArenaBattleDetailInfoOk>(GameServerCommand.GS_GET_RAINKING_BOARD_ARENA_BATTLE_DETAIL_REQUEST,new GsGetRankingBoardArenaBattleDetailInfoRequest{targetUid=p.Uid,rankingBoardType=BscTable.RANKING_BOARD_TYPE.RANKING_BOARD_ARENA_BATTLE_REAL_TIME});
            Check(preview.battleCardInfoList.Any(x=>x.cardSeq==1&&x.cardSkillEnchant.SequenceEqual(p.CardStates[1].SkillEnchants)),"Opponent battle snapshot carries enchant levels instead of zeroes");
            await CheckFragmentPolicies(runtime,peer,p,Check);
            finalGold=p.Gold;finalEnchants=p.CardStates[1].SkillEnchants.ToList();runtime.Store.Save();
        }
        await using(var restarted=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false)) {
            var p=restarted.Store.All().Single();Check(p.Gold==finalGold&&p.CardStates[1].SkillEnchants.SequenceEqual(finalEnchants)&&p.GrowthReceipts.Count>0,"Restart preserves gold, enchanted slots and mutation receipts");
        }
        string serverCoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(ProtocolRouter).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText("I:/BnsmModernWork/Verification/skill-enchant-server-latest.json",JsonSerializer.Serialize(new{passed=true,checks,folder,productionSaveUsed=false,serverCoreSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("SKILL_ENCHANT_SERVER_PASSED "+checks.Count);
    }

    private static async Task CheckFragmentPolicies(LocalRuntime runtime,Peer peer,LocalPlayer p,Action<bool,string> check)
    {
        const uint universal=LocalCardRegistry.UniversalFragmentId;
        var c=runtime.Catalog;
        int[] restricted=[LocalStoryChapter.MaknaeCardId,LocalHongCard.FirstCardId];
        check(c.UsableHeroes.All(card=>LocalCardRegistry.AllowsUniversalFragments(card)==!restricted.Contains(card.EvolveFirstAncestor)),
            "Only the two requested families reject universal substitution at every star rank");
        p.Level=99;
        p.DebugFlags=0;
        p.Gold=100000000;
        p.Inventory[universal]=100000;
        ulong seq=(ulong)p.CardIds.Count+1;
        p.CardIds.Add(0);
        string State()=>JsonSerializer.Serialize(new{p.Gold,p.Inventory,p.CardIds,p.CardStates,p.GrowthReceipts});
        foreach(int family in restricted)
        {
            var card=c.UsableHeroes.First(x=>x.EvolveFirstAncestor==family&&x.Rank==8&&x.RankUpCardId>0);
            uint piece=(uint)card.PieceId;
            var rule=c.SkillEnchants[card.SkillEnchantGroupId[0]].SkillEnchantInfoList.Single(x=>x.EnchantLevel==1);
            int required=rule.MaterialItemQuantity[rule.MaterialItemId.IndexOf(card.PieceId)];
            p.CardIds[(int)seq-1]=(uint)card.Id;
            p.CardStates[seq]=new(){Level=99,Exp=ProgressionRules.CardFloor(c,99),Enchant=9,ActiveSkills=[76,76],PassiveSkills=[76,76,76,76,76]};
            for(int i=0;i<rule.MaterialItemId.Count;i++)p.Inventory[(uint)rule.MaterialItemId[i]]=rule.MaterialItemQuantity[i];
            var enchant=new GsSkillEnchantRequest{cardSeq=seq,skillType=0};
            p.Inventory[piece]=required-1;
            string before=State();
            var failure=await peer.Request<FailPacket>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST,enchant,success:false);
            check(State()==before&&failure.message.Contains(LocalCardRegistry.UniversalFragmentsDisabledMessage),
                $"Family {family} rejects skill enchant substitution without changing inventory, skill levels or receipts");
            p.Inventory[piece]=required;
            var enchanted=await peer.Request<GsSkillEnchantOk>(GameServerCommand.GS_SKILL_ENCHANT_REQUEST,enchant);
            check(enchanted.cardInfo.cardSkillEnchant[0]==1&&p.Inventory[universal]==100000&&p.Inventory[piece]==0,
                $"Family {family} enchants with its own fragments");

            for(int i=0;i<card.RankUpMaterialItemId.Count;i++)p.Inventory[(uint)card.RankUpMaterialItemId[i]]=card.RankUpMaterialItemCount[i];
            int evolutionPieces=c.CardRanks.Single(x=>x.Rank==card.Rank).NextEVP;
            var evolve=new GsCardEvolutionRequest{targetCardSeq=seq};
            p.Inventory[piece]=evolutionPieces-1;
            before=State();
            failure=await peer.Request<FailPacket>(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve,success:false);
            check(State()==before&&failure.message.Contains(LocalCardRegistry.UniversalFragmentsDisabledMessage),
                $"Family {family} rejects star evolution with a universal stack covering the shortage");
            p.Inventory[piece]=evolutionPieces;
            var evolved=await peer.Request<GsCardEvolutionOk>(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve);
            check(evolved.evolutionCardInfo.cardID==card.RankUpCardId&&p.Inventory[universal]==100000&&p.Inventory.GetValueOrDefault(piece)==0,
                $"Family {family} evolves normally with its own fragments");
        }
    }
    internal static void Audit(string root)
    {
        var c=new GameCatalog(root);
        var report=new {
            playerLevel=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SKILL_ENCHANT),
            enchantLimit=c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_SKILL_ENCHANT),
            cardEnchant=c.Numbers[BscTable.NUMBER_TYPE.SKILL_ENCHANT_LIMIT_CARD_ENCHANT_GRADE],
            ranks=c.UsableHeroes.Where(x=>x.SkillEnchantGroupId.Any(id=>id>0)).GroupBy(x=>x.Rank).Select(g=>new{rank=g.Key,count=g.Count()}),
            examples=c.UsableHeroes.Where(x=>x.SkillEnchantGroupId.Any(id=>id>0)).Take(4).Select(x=>new{x.Id,x.__Name,x.Rank,x.ActiveSkillId,x.PassiveSkillId,x.SkillEnchantGroupId}),
            rules=c.SkillEnchants.Values.SelectMany(g=>g.SkillEnchantInfoList.Select(x=>new{group=g.SkillEnchantGroupID,x.EnchantLevel,x.PrevEnchantSkillId,x.NextEnchantSkillId,nextSkillEnchant=c.Skills.GetValueOrDefault(x.NextEnchantSkillId)?.EnchantLevel,x.MoneyType,x.Cost,x.Prob,x.MaterialItemId,x.MaterialItemQuantity})).ToArray()
        };
        File.WriteAllText("I:/BnsmModernWork/Verification/skill-enchant-audit.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{report.playerLevel,report.cardEnchant,report.enchantLimit,report.ranks,rules=report.rules.Length,probabilities=report.rules.Select(x=>x.Prob).Distinct(),currencies=report.rules.Select(x=>x.MoneyType).Distinct()}));
    }
}
