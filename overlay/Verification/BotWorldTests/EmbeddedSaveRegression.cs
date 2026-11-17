#if BNSM_EMBEDDED_TESTS
using Bnsm.Server;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;

internal static class EmbeddedSaveRegression
{
    public static async Task Run(string root)
    {
        string evidence="I:/BnsmModernWork/Verification/android-manager-contract";
        using var java=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"java-result.json")));
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        string directory=Path.Combine(evidence,"csharp-"+Guid.NewGuid().ToString("N"));
        var manager=new ManagedSaveWorkspace(directory);
        manager.Execute("list");
        var imported=manager.Execute("import",name:"Android exported profile",file:java.RootElement.GetProperty("export").GetString());
        string id=imported.GetProperty("profile").GetProperty("Id").GetString()!;manager.Execute("switch",id:id);
        using var session=manager.Open();
        Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(session.Directory,"players.json"))).RootElement.GetArrayLength()==0,"Android archive imports through authoritative checksum/schema validation");
        var androidWorkspace=new ManagedSaveWorkspace(Path.Combine(java.RootElement.GetProperty("root").GetString()!,"EmbeddedServer/Saves"));
        using(var androidSession=androidWorkspace.Open())Check(androidSession.Id==java.RootElement.GetProperty("firstId").GetString(),"Authoritative reader opens Java-created and switched workspace");
        var catalog=new GameCatalog(root);var store=new PlayerStore(session.Directory,catalog);var player=store.GetOrCreate("checkpoint-fixture");
        long writes=store.SaveWrites;string path=Path.Combine(session.Directory,"players.json");byte[] before=File.ReadAllBytes(path);DateTime modified=File.GetLastWriteTimeUtc(path);
        var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<30;i++)store.Checkpoint();watch.Stop();
        Check(store.SaveWrites==writes&&File.GetLastWriteTimeUtc(path)==modified&&File.ReadAllBytes(path).SequenceEqual(before),"Thirty manual checkpoints preserve committed bytes without graph serialization or replacement");
        lock(store.Gate){player.Gold=123456;store.Save();}store.Checkpoint();
        Check(new PlayerStore(session.Directory,catalog).Find(player.Uid)!.Gold==123456,"Committed mutation survives checkpoint and reload");
        File.Delete(path);store.Checkpoint();Check(File.Exists(path)&&new PlayerStore(session.Directory,catalog).Find(player.Uid)!.Gold==123456,"Checkpoint repairs a missing committed file");
        string liveExport=Path.Combine(evidence,"live-"+Guid.NewGuid().ToString("N")+".bnsm-save.zip");
        await using(var server=new EmbeddedServer(root,session.Directory,new(){DirectoryPort=0,LoginPort=0,GamePort=0}))
        {
            var snapshot=server.CaptureSave();string export=liveExport;manager.ExportSnapshot(session,export,snapshot);
            using var socket=new System.Net.Sockets.TcpClient();await socket.ConnectAsync("127.0.0.1",server.DirectoryPort);
            Check(socket.Connected,"Live export retains the same server and open listening socket");
            using var zip=ZipFile.OpenRead(export);using var manifest=JsonDocument.Parse(zip.GetEntry("archive.json")!.Open());
            Check(manifest.RootElement.GetProperty("Files").GetProperty("players.json").GetString()==Convert.ToHexString(SHA256.HashData(snapshot["players.json"])),"Live export binds immutable captured bytes to original manifest");
            var target=new ManagedSaveWorkspace(Path.Combine(evidence,"restore-"+Guid.NewGuid().ToString("N")));target.Execute("list");
            var restored=target.Execute("import",file:export);target.Execute("switch",id:restored.GetProperty("profile").GetProperty("Id").GetString());using var reopened=target.Open();
            Check(new PlayerStore(reopened.Directory,catalog).Find(player.Uid)!.Gold==123456,"Live snapshot restores through the original save validator");
        }
        string hostFiles=Path.Combine(evidence,"console-"+Guid.NewGuid().ToString("N")),hostRoot=Path.Combine(hostFiles,"EmbeddedServer"),hostEvidence=Path.Combine(hostFiles,"external/Diagnostics/embedded-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(hostEvidence)!);
        var hostWorkspace=new ManagedSaveWorkspace(Path.Combine(hostRoot,"Saves"));
        var hostImported=hostWorkspace.Execute("import",file:liveExport);hostWorkspace.Execute("switch",id:hostImported.GetProperty("profile").GetProperty("Id").GetString());
        string supportExport=Path.Combine(hostFiles,"embedded-save-"+Guid.NewGuid().ToString("N")+".bnsm-save.zip");
        string noSave=await Bnsm.UnityHost.Host.ExportForSupport(supportExport);
        Check(!string.IsNullOrEmpty(noSave)&&!File.Exists(supportExport),"Unready support export returns an explicit omission without creating a pretend save");
        try{
            Task starting=Bnsm.UnityHost.Host.Start(File.ReadAllBytes("I:/BnsmModernWork/Builds/PhoneServer/server-content.zip"),hostRoot,hostEvidence);
            Task<string> capture=Bnsm.UnityHost.Host.ExportForSupport(supportExport);await starting;
            Check(string.IsNullOrEmpty(await capture)&&File.Exists(supportExport),"Support export queries host readiness after serialized startup and captures the save");
            using(var status=JsonDocument.Parse(JsonSerializer.Serialize(Bnsm.UnityHost.Host.Snapshot()))){
                var timing=status.RootElement.GetProperty("lastSupportExport");
                Check(timing.GetProperty("compression").GetString()=="Fastest"&&timing.GetProperty("archiveBytes").GetInt64()==new FileInfo(supportExport).Length&&timing.GetProperty("captureMilliseconds").GetDouble()>=0&&timing.GetProperty("gateWaitMilliseconds").GetDouble()>=0,"Interactive export uses fast compression and records actual capture/archive/wait timings");
            }
            using(var zip=ZipFile.OpenRead(supportExport)){
                Check(zip.GetEntry("players.json")!=null&&zip.GetEntry("bot-world.json")!=null&&zip.GetEntry("save-format.json")!=null&&zip.GetEntry("archive.json")!=null,"Full host support snapshot includes player progress and resident world");
                using var saved=JsonDocument.Parse(zip.GetEntry("players.json")!.Open());
                Check(saved.RootElement.EnumerateArray().Any(p=>p.GetProperty("Uid").GetUInt32()==player.Uid&&p.GetProperty("Gold").GetUInt64()==123456),"Console snapshot contains actual committed player currency");
            }
            using(var socket=new System.Net.Sockets.TcpClient()){await socket.ConnectAsync("127.0.0.1",10111);Check(socket.Connected&&Bnsm.UnityHost.Host.Ready,"Support capture preserves the running game session and listeners");}
            bool failed=false;try{await Bnsm.UnityHost.Host.ExportForSupport(hostFiles);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
            Check(failed&&Bnsm.UnityHost.Host.Ready,"A live snapshot write failure propagates instead of silently producing logs only");
        }finally{await Bnsm.UnityHost.Host.Stop();}
        string stopped=Path.Combine(hostFiles,"stopped.bnsm-save.zip");
        Check(!string.IsNullOrEmpty(await Bnsm.UnityHost.Host.ExportForSupport(stopped))&&!File.Exists(stopped),"A stopped host also reports an explicit omission without copying live files");
        string coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(EmbeddedServer).Assembly.Location))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(evidence,"csharp-result.json"),JsonSerializer.Serialize(new{passed=true,checks,coreSha256,checkpointTotalMilliseconds=watch.Elapsed.TotalMilliseconds,supportExport,hostFiles},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS embedded save regression: "+checks.Count);
    }
}
#endif
