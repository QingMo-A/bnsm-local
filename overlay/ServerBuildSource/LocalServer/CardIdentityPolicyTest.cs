using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

internal static class CardIdentityPolicyTest
{
    public static void Run(string root)
    {
        var checks=new List<string>();
        void Assert(bool condition,string label) {if(!condition)throw new Exception(label);checks.Add(label);}
        var catalog=new GameCatalog(root,true);
        int rank=GachaRules.Load(root).CardRank;
        var gold=catalog.DrawPool(false,rank);
        var cash=catalog.DrawPool(true,rank);
        using var availability=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/card-resource-availability.json")));
        var missing=availability.RootElement.GetProperty("excludedCardIds").EnumerateArray().Select(x=>x.GetInt32()).ToHashSet();
        Assert(!gold.Concat(cash).Any(c=>missing.Contains(c.Id)),"Original records with missing same-version portraits/atlases cannot be drawn");
        Assert(gold.Length>0 && cash.Length>0,"Original-rank gold and diamond pools retain collectible heroes");
        Assert(gold.Concat(cash).All(c=>c.Talent!=BscTable.CARD_TALENT.CARD_TALENT_X && c.CollectionId>0),"No X placeholder or missing-collection entry can be drawn");
        Assert(cash.Any(c=>c.Talent>=BscTable.CARD_TALENT.CARD_TALENT_S),"Ten-draw guarantee has valid initial S/SS candidates");
        Assert(catalog.StarterCards().All(id=>catalog.InitialHeroes.Any(c=>c.Id==id)),"Starter cards use one genuine initial form per original evolution chain");
        var xia=catalog.Cards.Single(c=>c.Id==60013);
        Assert(xia.Talent==BscTable.CARD_TALENT.CARD_TALENT_X && xia.CollectionId==0 && xia.Minimi=="3DCH2008" && xia.CardSmall=="CardSm_EP_0005_2","Reported Xia Ni is the original X/zero-collection template with a reused illustration");
        var xiaRepair=CardIdentityPolicy.Replacement(catalog,xia)!;
        Assert(xiaRepair.Id==60023 && xiaRepair.Rank==3 && xiaRepair.Minimi==xia.Minimi && xiaRepair.CardSmall=="CardSm_EP_0008_0","Reported template has a same-class, same-star, same-model collectible replacement with its own portrait");
        string original=Path.Combine(root,"LocalServer/saves/players.json");byte[] productionSource=File.ReadAllBytes(original);
        var fixture=JsonSerializer.Deserialize<List<LocalPlayer>>(productionSource)!;
        if(fixture.Count==0)fixture.Add(new LocalPlayer {Uid=1001,Account="identity-baseline",CardIds=catalog.StarterCards()});
        foreach(var p in fixture) {ProgressionRules.RepairLegacy(catalog,p);p.SkillPoint??=catalog.SkillPointMax(p);if(p.SkillPointStamp==0)p.SkillPointStamp=ProtocolRouter.Now;}
        fixture.Add(new LocalPlayer {Uid=fixture.Max(x=>x.Uid)+1,Account="identity-fixture",CardIds=[60013,60014,20154],SkillPoint=10,SkillPointStamp=ProtocolRouter.Now});
        byte[] source=JsonSerializer.SerializeToUtf8Bytes(fixture);
        var before=JsonSerializer.Deserialize<List<LocalPlayer>>(source)!;
        string directory=Path.Combine(root,"verification/card-identities",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,"players.json"),source);
        var store=new PlayerStore(directory,catalog);
        int count=0;
        foreach(var old in before) {
            var restored=store.Find(old.Uid)!;var plan=CardIdentityPolicy.Plan(catalog,old);
            foreach(var repair in plan) {
                var previous=catalog.Cards.Single(c=>c.Id==repair.PreviousCardId);var current=catalog.Cards.Single(c=>c.Id==repair.CardId);
                Assert(previous.Talent==BscTable.CARD_TALENT.CARD_TALENT_X && current.Rank==previous.Rank && current.Class==previous.Class && current.Minimi==previous.Minimi && restored.CardIds[(int)repair.Sequence-1]==repair.CardId,
                    "Repair preserves sequence, stars, class and model for card "+repair.Sequence);
                count++;
            }
            Assert(old.CardIds.Count==restored.CardIds.Count,"Repair retains card count for player "+old.Uid);
            for(int i=0;i<old.CardIds.Count;i++)if(!plan.Any(p=>p.Sequence==(ulong)i+1))
                if(old.CardIds[i]!=restored.CardIds[i])throw new Exception("Unrelated card changed");
            checks.Add("All unrelated cards retain their exact original IDs for player "+old.Uid);
            JsonObject a=JsonSerializer.SerializeToNode(old)!.AsObject(),b=JsonSerializer.SerializeToNode(restored)!.AsObject();
            foreach(var field in a)if(field.Key is not ("CardIds" or "CardIdentityRepairs" or "CollectedCardIds"))
                if(!JsonNode.DeepEquals(field.Value,b[field.Key]))throw new Exception("Repair changed "+field.Key);
            checks.Add("Balances, XP, per-card state, decks, helper, gacha, charge orders and progress stay unchanged for player "+old.Uid);
            Assert(restored.CardIdentityRepairs.Count==old.CardIdentityRepairs.Count+plan.Count,"Every replacement retains an original-ID audit record");
            Assert(CardIdentityPolicy.Plan(catalog,restored).Count==0,"Applying repair again cannot replace the same card twice");
        }
        var backups=Directory.GetFiles(directory,"players.json.before-card-identity-fix-*");
        Assert(count>0 && backups.Length==1 && File.ReadAllBytes(backups[0]).SequenceEqual(source),"Original save is preserved byte-for-byte before the first identity repair");
        _=new PlayerStore(directory,catalog);
        Assert(Directory.GetFiles(directory,"players.json.before-card-identity-fix-*").Length==1,"Second startup does not create duplicate migrations or backups");
        Assert(File.ReadAllBytes(original).SequenceEqual(productionSource),"Production save is untouched by repair verification");
        var untouchedS=catalog.Cards.Single(c=>c.Id==20154);
        var probe=new LocalPlayer {CardIds=[(uint)untouchedS.Id],CardStates=[]};
        Assert(CardIdentityPolicy.Plan(catalog,probe).Count==0,"A zero-collection S card is never automatically downgraded or replaced");
        var skinned=new LocalPlayer {CardIds=[60013],CardStates=new() {[1]=new LocalCardState {Skin=1000}}};
        Assert(CardIdentityPolicy.Plan(catalog,skinned).Count==0,"An exceptional legacy skin requires manual handling instead of silent loss");
        var report=new {completedAt=DateTimeOffset.Now,passed=checks.Count,checks,repairedCardsOnCopy=count,
            isolatedDirectory=directory,originalSaveUnchanged=true,originalSaveSha256=Convert.ToHexString(SHA256.HashData(productionSource)).ToLowerInvariant(),
            eligibleGoldCards=gold.Length,eligibleCashCards=cash.Length};
        File.WriteAllText(Path.Combine(root,"evidence/card-identity-policy-current-test.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"PASS: {checks.Count} card identity policy checks; {count} placeholders repaired only on an isolated save copy.");
    }
}
