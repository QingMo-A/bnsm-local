using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Bnsm.Server;

internal static class BotManagementRegression
{
    public static void Run(string root)
    {
        const string evidence="I:/BnsmModernWork/Verification/android-manager-contract";
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"java-result.json")));
        var manager=new ManagedSaveWorkspace(Path.Combine(evidence,"bots-"+Guid.NewGuid().ToString("N")));manager.Execute("list");
        var imported=manager.Execute("import",file:native.RootElement.GetProperty("botArchive").GetString());
        manager.Execute("switch",id:imported.GetProperty("profile").GetProperty("Id").GetString());using var session=manager.Open();
        var catalog=new GameCatalog(root);string path=Path.Combine(session.Directory,"bot-world.json");
        LocalBotWorld Open()=>new(root,session.Directory,catalog,Array.Empty<LocalPlayer>());
        var world=Open();var residents=world.All().ToArray();
        Check(residents.Length==16&&residents.Skip(12).All(b=>b.Strength=="expert"),"Java queued generation uses original seed population and requested native loadouts");
        var doc=JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Check(doc["ManagementRequests"]!.AsArray().Count==0&&doc["ManagementResults"]!.AsArray().Single()!["Applied"]!.GetValue<bool>(),"Queue removal and success receipt commit with generated residents");
        Check(Open().All().Select(b=>b.Uid).SequenceEqual(residents.Select(b=>b.Uid)),"Restart cannot generate the same four residents twice");
        string untouched=JsonSerializer.Serialize(residents[1]);var original=residents[0];
        void Queue(params BotManagementRequest[] requests){var node=JsonNode.Parse(File.ReadAllText(path))!.AsObject();node["ManagementRequests"]=JsonSerializer.SerializeToNode(requests);File.WriteAllText(path,node.ToJsonString());}
        BotManagementRequest Configure(int revision)=>new(){Id=Guid.NewGuid().ToString("N"),Action="configure",BotUid=original.Uid,ExpectedRevision=revision,Strength="legend",Style="control",AiStrength="casual"};
        Queue(Configure(original.Revision));world=Open();var changed=world.Find(original.Uid)!;
        Check(changed.Revision==original.Revision+1&&changed.Strength=="legend"&&changed.Style=="control"&&changed.AiStrength=="casual","Offline strength/style/reserved AI settings apply through the original builder");
        Check(changed.Uid==original.Uid&&changed.Seed==original.Seed&&changed.CreatedAt==original.CreatedAt&&JsonSerializer.Serialize(world.Find(residents[1].Uid))==untouched,"Identity, seed and unrelated resident profiles are retained");
        string configured=JsonSerializer.Serialize(changed);Queue(Configure(original.Revision));world=Open();
        Check(JsonSerializer.Serialize(world.Find(original.Uid))==configured,"A stale offline revision cannot overwrite a newer loadout");
        doc=JsonNode.Parse(File.ReadAllText(path))!.AsObject();Check(!doc["ManagementResults"]!.AsArray().Last()!["Applied"]!.GetValue<bool>(),"Rejected settings retain a visible failure receipt without blocking game startup");
        var add=new BotManagementRequest{Id=Guid.NewGuid().ToString("N"),Action="generate",Count=4,Strength="casual"};Queue(add,add);world=Open();
        Check(world.All().Count==20&&Open().All().Count==20,"Duplicate generation IDs and repeated startup do not duplicate residents");
        Queue(new BotManagementRequest{Id=Guid.NewGuid().ToString("N"),Action="generate",Count=100,Strength="expert"});world=Open();
        Check(world.All().Count==20,"Population cap rejects excessive generation without partial additions");
        var bad=Configure(changed.Revision);bad.Strength="invalid";Queue(bad);world=Open();
        Check(JsonSerializer.Serialize(world.Find(original.Uid))==configured,"Invalid offline options do not alter builds or prevent startup");
        string coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(EmbeddedServer).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(evidence,"bot-result.json"),JsonSerializer.Serialize(new{passed=true,checks,coreSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS offline bot management: "+checks.Count);
    }
}
