using Bnsm.Server;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

internal static class PhoneSupportRegression
{
    public static async Task Run(string root)
    {
        string fixture=Path.Combine("I:/BnsmModernWork/Verification","phone-support-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        var checks=new List<string>();
        void Check(bool condition,string text){if(!condition)throw new InvalidDataException(text);checks.Add(text);}
        void Reject(Action action,string text){bool refused=false;try{action();}catch(Exception error)when(error is IOException or InvalidDataException or JsonException or ArgumentException){refused=true;}Check(refused,text);}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string logFolder=Path.Combine(fixture,"logs");var stdout=Console.Out;var stderr=Console.Error;
        Console.SetOut(TextWriter.Null);Console.SetError(TextWriter.Null);
        try {
            var logs=ServerDiagnostics.Begin(logFolder);
            await using(logs) {
                Console.WriteLine("console-capture-marker loginSessionKey=secret-session password=secret-password token=secret-token");
                Console.Error.WriteLine("console-error-marker");
                Check(logs.Tail().Contains("console-capture-marker")&&logs.Tail().Contains("console-error-marker"),"Captures normal output and errors for the native log viewer");
                Check(!logs.Tail().Contains("secret-session")&&!logs.Tail().Contains("secret-password")&&!logs.Tail().Contains("secret-token"),"Session keys, passwords and labelled tokens are redacted");
                Parallel.For(0,20000,i=>ServerDiagnostics.Info("concurrent-record-"+i));
                Check(logs.Tail(500).Split('\n').Length<=500,"Concurrent writers retain at most 500 recent lines");
                for(int i=0;i<3000;i++){logs.Write("INFO",new string('x',4000));if(i%50==0)await Task.Delay(10);}
                logs.Write("INFO","http-log-marker");
                string legacy=Path.Combine(fixture,"legacy-played-profile");
                await using(var runtime=new LocalRuntime(root,legacy,0,0,0,false)) {
                    var player=runtime.Store.GetOrCreate("played-local-player");player.Level=72;player.Gold=456789;runtime.Store.Save();
                    var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
                    await using var app=builder.Build();DebugApi.Map(app,runtime);await app.StartAsync();
                    string address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();using var http=new HttpClient{BaseAddress=new Uri(address)};
                    Check((await http.GetStringAsync("/diagnostics")).Contains("导出日志 ZIP"),"Windows diagnostics page exposes refresh and export controls");
                    using var state=JsonDocument.Parse(await http.GetStringAsync("/debug/logs"));
                    Check(state.RootElement.GetProperty("ok").GetBoolean()&&state.RootElement.GetProperty("text").GetString()!.Contains("http-log-marker"),"HTTP log viewer reads the same captured server records");
                    using var zip=new ZipArchive(new MemoryStream(await http.GetByteArrayAsync("/debug/logs/export")));
                    Check(zip.Entries.Any(e=>e.FullName=="diagnostics.json")&&zip.Entries.All(e=>!e.FullName.Contains("players")&&!e.FullName.Contains("bot-world")),"HTTP log export is a valid ZIP and excludes game saves");
                    await app.StopAsync();
                }
                string before=Hash(Path.Combine(legacy,"players.json"));string botsBefore=Hash(Path.Combine(legacy,"bot-world.json"));
                var manager=new ManagedSaveWorkspace(Path.Combine(fixture,"save-manager"),legacy);
                var snapshot=manager.Execute("list");string original=snapshot.GetProperty("activeProfileId").GetString()!;
                string OriginalSaves=Path.Combine(manager.Root,"profiles",original,"saves");
                Check(Hash(Path.Combine(OriginalSaves,"players.json"))==before&&Hash(Path.Combine(OriginalSaves,"bot-world.json"))==botsBefore,"First phone manager launch copies played progress and bot roster byte for byte");
                Check(Hash(Path.Combine(legacy,"players.json"))==before,"Original phone profile remains untouched");
                Check(manager.Execute("list").GetProperty("profiles").GetArrayLength()==1,"Repeated manager initialization never duplicates the old profile");
                using(var session=manager.Open()) {
                    Check(session.Id==original&&session.Directory==OriginalSaves,"Running server opens the selected managed profile and holds its lease");
                    Reject(()=>manager.Execute("new",name:"unsafe"),"New profile is blocked while the server owns a save lease");
                    Reject(()=>manager.Execute("backup"),"Backup/export is blocked while a live save may be updating");
                    Reject(()=>manager.Open(),"Two hosts cannot open the same workspace concurrently");
                }
                manager.Execute("rename",id:original,name:"试玩进度 中文");
                Check(manager.Execute("list").GetProperty("profiles")[0].GetProperty("Name").GetString()=="试玩进度 中文","Phone manager supports Unicode profile names");
                var fresh=manager.Execute("new",name:"新冒险");string freshId=fresh.GetProperty("profile").GetProperty("Id").GetString()!;
                Check(manager.Execute("list").GetProperty("activeProfileId").GetString()==freshId&&File.ReadAllText(Path.Combine(manager.Root,"profiles",freshId,"saves/players.json"))=="[]","New game selects an independent empty profile");
                manager.Execute("switch",id:original);manager.Execute("backup",id:original);
                Check(manager.Backups(original).Count>=2,"Startup and manual backups are listed for the selected profile");
                string exported=Path.Combine(fixture,"portable.bnsm-save.zip");manager.Execute("export",id:original,file:exported);
                var desktop=new ManagedSaveWorkspace(Path.Combine(fixture,"desktop-manager"));
                var imported=desktop.Execute("import",name:"手机导入",file:exported);string desktopId=imported.GetProperty("profile").GetProperty("Id").GetString()!;
                Check(Hash(Path.Combine(desktop.Root,"profiles",desktopId,"saves/players.json"))==before&&Hash(Path.Combine(desktop.Root,"profiles",desktopId,"saves/bot-world.json"))==botsBefore,"Phone exports import through the desktop manager with exact player and bot state");
                string desktopExport=Path.Combine(fixture,"desktop.bnsm-save.zip");desktop.Execute("export",id:desktopId,file:desktopExport);
                var back=manager.Execute("import",file:desktopExport);string backId=back.GetProperty("profile").GetProperty("Id").GetString()!;
                Check(manager.Execute("list").GetProperty("activeProfileId").GetString()==original&&Hash(Path.Combine(manager.Root,"profiles",backId,"saves/players.json"))==before,"Desktop-to-phone import creates a new slot without replacing the active game");
                File.WriteAllText(Path.Combine(OriginalSaves,"players.json"),"[]");manager.Execute("restore",id:original,file:exported);
                Check(Hash(Path.Combine(OriginalSaves,"players.json"))==before&&manager.Backups(original).Any(b=>b.Name.Contains("before-restore")),"Restore recovers exact progress and backs up the replaced state first");
                manager.Execute("archive",id:freshId);
                Check(manager.AllBackups().Any(b=>b.Name.Contains("before-archive")),"Archived profiles remain recoverable through the all-backups picker");
                string bad=Path.Combine(fixture,"bad-bot-import");Directory.CreateDirectory(bad);File.WriteAllText(Path.Combine(bad,"players.json"),"[]");File.WriteAllText(Path.Combine(bad,"bot-world.json"),"{\"Schema\":1,\"Bots\":[{\"Uid\":1002}]}");
                int count=manager.Execute("list").GetProperty("profiles").GetArrayLength();
                Reject(()=>manager.Execute("import",file:bad),"Malformed bot identities are rejected before importing a profile");
                Check(manager.Execute("list").GetProperty("profiles").GetArrayLength()==count&&Hash(Path.Combine(OriginalSaves,"players.json"))==before,"Failed import leaves all existing slots and active progress intact");
                string slip=Path.Combine(fixture,"traversal.zip");using(var zip=ZipFile.Open(slip,ZipArchiveMode.Create))using(var text=new StreamWriter(zip.CreateEntry("../outside.json").Open()))text.Write("[]");
                Reject(()=>manager.Execute("import",file:slip),"Save archive paths cannot escape the import staging directory");
                Reject(()=>manager.Backups("../../outside"),"Backup selection validates profile identifiers");
                using(var reopened=new ManagedSaveWorkspace(manager.Root,legacy).Open())Check(reopened.Id==original,"Manager selection persists across a new host instance");
            }
            Check(Directory.GetFiles(logFolder,"*.log").Length<=4&&Directory.GetFiles(logFolder,"*.log").Length>=2,"Persistent logs rotate instead of growing without limit");
            Check(Directory.GetFiles(logFolder,"*.log").All(p=>new FileInfo(p).Length<=2*1024*1024),"Each retained log stays within its 2 MiB limit");
            await using(var restart=ServerDiagnostics.Begin(logFolder))Check(restart.Tail().Contains("Service process log closing"),"Reopening the server retains recent records from the previous process");
            string unavailable=Path.Combine(fixture,"not-a-directory");File.WriteAllText(unavailable,"occupied");
            await using(var fallback=ServerDiagnostics.Begin(unavailable)) {
                fallback.Write("ERROR","disk-unavailable-marker");
                using var zip=new ZipArchive(new MemoryStream(fallback.Export()));
                Check(fallback.Tail().Contains("disk-unavailable-marker")&&zip.GetEntry("recent-tail.log")!=null,"Disk failures preserve viewable and exportable in-memory diagnostics");
            }
        } finally {Console.SetOut(stdout);Console.SetError(stderr);}
        File.WriteAllText("I:/BnsmModernWork/Verification/phone-support-regression.json",JsonSerializer.Serialize(new{passed=true,checks,fixture,realSaveChanged=false,deviceImeVerified=false,deviceBackgroundStressVerified=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PHONE_SUPPORT_REGRESSION_PASSED "+checks.Count);
    }
}
