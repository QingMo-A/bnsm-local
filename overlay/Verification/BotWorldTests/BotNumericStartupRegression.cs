using System.Text.Json;
using System.Security.Cryptography;
using System.Net.Sockets;
using Bnsm.Server;

internal static class BotNumericStartupRegression
{
    private const string Proof="I:/BnsmModernWork/Verification/android-manager-contract/numeric-fixture.json";
    internal static async Task Prepare(string root)
    {
        string files="I:/BnsmModernWork/Verification/android-manager-contract/numeric-"+Guid.NewGuid().ToString("N");
        var manager=new ManagedSaveWorkspace(Path.Combine(files,"EmbeddedServer/Saves"));manager.Execute("list");
        string id,directory;
        using(var session=manager.Open()){
            id=session.Id;directory=session.Directory;
            await using(var runtime=new LocalRuntime(root,directory,0,0,0)){
                FactionRegression.Prepare(runtime);runtime.Store.BackfillMissingStageStars();runtime.Store.Save();
            }
            // Complete ordinary first-load tutorial/skill-point migrations
            // before comparing byte preservation across bot management.
            await using(var normalized=new LocalRuntime(root,directory,0,0,0)){normalized.Store.Save();}
        }
        File.Copy(Path.Combine(directory,"bot-world.json"),Path.Combine(files,"world-before.json"));
        File.Copy(Path.Combine(directory,"players.json"),Path.Combine(files,"players-before.json"));
        File.WriteAllText(Proof,JsonSerializer.Serialize(new{files,id,directory}));
    }
    internal static async Task Verify(string root)
    {
        using var meta=JsonDocument.Parse(File.ReadAllText(Proof));string files=meta.RootElement.GetProperty("files").GetString()!,directory=meta.RootElement.GetProperty("directory").GetString()!;
        string path=Path.Combine(directory,"bot-world.json"),evidence=Path.Combine(files,"external/Diagnostics/embedded-state.json");Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);Console.WriteLine("PASS "+label);}
        byte[] human=File.ReadAllBytes(Path.Combine(directory,"players.json")),original=File.ReadAllBytes(Path.Combine(files,"world-before.json"));
        using var queued=JsonDocument.Parse(File.ReadAllBytes(path));
        Check(queued.RootElement.GetProperty("Bots")[0].GetProperty("Profile").GetProperty("TutorialStatus").GetUInt64()==ulong.MaxValue,"Actual AOSP queue retains the native UInt64 tutorial bitset");
        Check(human.SequenceEqual(File.ReadAllBytes(Path.Combine(files,"players-before.json"))),"Native bot edits leave human save bytes unchanged");
        var host=typeof(EmbeddedServer).Assembly.GetType("Bnsm.UnityHost.Host",true)!;
        byte[] archive=File.ReadAllBytes("I:/BnsmModernWork/Builds/PhoneServer/server-content.zip");
        Task Start()=> (Task)host.GetMethod("Start")!.Invoke(null,new object[]{archive,Path.Combine(files,"EmbeddedServer"),evidence})!;
        Task Stop()=> (Task)host.GetMethod("Stop")!.Invoke(null,null)!;
        bool Ready()=> (bool)host.GetProperty("Ready")!.GetValue(null)!;
        int Count(){using var doc=JsonDocument.Parse(File.ReadAllBytes(path));return doc.RootElement.GetProperty("Bots").GetArrayLength();}
        byte[]? valid=null;
        try{
            await Start();Check(Ready()&&Count()==16,"Full embedded host boots from an AOSP-edited existing roster and adds exactly four residents");
            using(var socket=new TcpClient()){await socket.ConnectAsync("127.0.0.1",10111);Check(socket.Connected,"The initialized host accepts native directory connections");}
            await Stop();await Start();Check(Ready()&&Count()==16,"Reopening the game cannot replay an applied generation request");await Stop();
            Check(human.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"players.json"))),"Full startup and generation preserve the human profile exactly");
            byte[] damaged=File.ReadAllBytes(Path.Combine(files,"legacy-corrupted-world.json"));File.WriteAllBytes(path,damaged);
            bool strictRejected=false;try{SaveWorkspace.ValidateData(directory);}catch(JsonException){strictRejected=true;}
            Check(strictRejected,"The old Android rewrite reproduces the original server initialization failure");
            await Start();Check(Ready()&&Count()==16,"Startup recovers the known old numeric damage and still applies the pending command once");await Stop();
            string backup=Path.Combine(Path.GetDirectoryName(directory)!,"recovery","bot-world-"+Convert.ToHexString(SHA256.HashData(damaged)).ToLowerInvariant()+".json");
            Check(File.ReadAllBytes(backup).SequenceEqual(damaged),"Numeric recovery retains the exact original damaged file before replacing it");
            using(var repaired=JsonDocument.Parse(File.ReadAllBytes(path)))Check(repaired.RootElement.GetProperty("Bots").EnumerateArray().All(x=>x.GetProperty("Profile").GetProperty("TutorialStatus").GetUInt64()==ulong.MaxValue),"Recovered and generated residents retain their original tutorial sentinel");
            valid=File.ReadAllBytes(path);await Start();await Stop();Check(Count()==16,"A recovered profile also remains idempotent across another cold start");
            Check(human.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"players.json"))),"Recovery never rewrites human inventory or progress");
            // Known tutorial damage must not become a blanket license to repair
            // unrelated malformed numbers, reset a profile or drop residents.
            string unsafeText=System.Text.Encoding.UTF8.GetString(damaged);int seed=unsafeText.IndexOf("\"Seed\":",StringComparison.Ordinal)+7,end=unsafeText.IndexOf(',',seed);
            unsafeText=unsafeText[..seed]+"1.2E99"+unsafeText[end..];byte[] unrelated=System.Text.Encoding.UTF8.GetBytes(unsafeText);File.WriteAllBytes(path,unrelated);
            bool failed=false;try{await Start();}catch{failed=true;}
            Check(failed&&!Ready()&&File.ReadAllBytes(path).SequenceEqual(unrelated),"Unrelated numeric damage is rejected intact instead of guessing or clearing residents");
            using(var state=JsonDocument.Parse(File.ReadAllText(evidence)))Check(!state.RootElement.GetProperty("ready").GetBoolean()&&!string.IsNullOrWhiteSpace(state.RootElement.GetProperty("error").GetString()),"A failed startup writes its actual cause into offline diagnostic evidence");
            File.WriteAllBytes(path,valid);await Start();Check(Ready(),"The same host can restart successfully after an earlier initialization failure");await Stop();
        }finally{await Stop();if(valid!=null)File.WriteAllBytes(path,valid);}
        string coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(EmbeddedServer).Assembly.Location))).ToLowerInvariant();
        using(var imported=JsonDocument.Parse(File.ReadAllText(Path.Combine(files,"manager-import-info.json")))){
            string importedFiles=imported.RootElement.GetProperty("files").GetString()!, importedDirectory=imported.RootElement.GetProperty("directory").GetString()!;
            SaveWorkspace.ValidateData(importedDirectory);
            Check(human.SequenceEqual(File.ReadAllBytes(Path.Combine(importedDirectory,"players.json"))),"Manager import preserves real human progress bytes before startup");
            async Task OpenImported()=>await (Task)host.GetMethod("Start")!.Invoke(null,new object[]{archive,Path.Combine(importedFiles,"EmbeddedServer"),Path.Combine(importedFiles,"embedded-state.json")})!;
            try{
                await OpenImported();Check(Ready(),"Full embedded host initializes the native manager-imported populated save");await Stop();
                using(var world=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(importedDirectory,"bot-world.json"))))
                    Check(world.RootElement.GetProperty("Bots").GetArrayLength()==16,"Imported pending resident settings apply exactly once");
                await OpenImported();await Stop();
                Check(human.SequenceEqual(File.ReadAllBytes(Path.Combine(importedDirectory,"players.json"))),"Imported progress remains intact after restart");
            }finally{await Stop();}
            File.WriteAllText("I:/BnsmModernWork/Verification/android-manager-contract/import-core-result.json",JsonSerializer.Serialize(new{passed=true,coreSha256,checks=checks.TakeLast(4),productionSaveUsed=false,phoneUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        File.WriteAllText("I:/BnsmModernWork/Verification/android-manager-contract/numeric-result.json",JsonSerializer.Serialize(new{passed=true,checks,coreSha256,fixture=files,productionSaveUsed=false,phoneUsed=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
