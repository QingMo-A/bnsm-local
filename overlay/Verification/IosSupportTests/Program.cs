using System.IO.Compression;
using System.Text;
using System.Text.Json;

string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
string folder=Path.Combine(root,"Verification/ios-support-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string client=Path.Combine(folder,"client"),server=Path.Combine(folder,"server");Directory.CreateDirectory(client);Directory.CreateDirectory(server);
File.WriteAllText(Path.Combine(client,"client.log"),"client-original\n");
File.WriteAllText(Path.Combine(server,"server.log"),"server-original\n");
File.WriteAllText(Path.Combine(client,"players.json"),"must never export an arbitrary live save");
string state=Path.Combine(folder,"embedded-state.json");File.WriteAllText(state,"{\"ready\":false,\"error\":\"original startup failure\"}");
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
string logs=LocalMobileSupportArchive.Create(folder,client,server,state,null,"{\"saveFilesIncluded\":false}");
using(var zip=ZipFile.OpenRead(logs)){
 Check(zip.GetEntry("save.bnsm-save.zip")==null,"Unready startup exports no save snapshot");
 Check(zip.GetEntry("host-state.json")!=null,"Startup evidence retained");
 Check(zip.GetEntry("logs/client/client.log")!=null&&zip.GetEntry("logs/server/server.log")!=null,"Client and server logs share one archive");
 Check(zip.Entries.All(e=>!e.FullName.EndsWith("players.json")),"Arbitrary live profile files never copied");
 using var info=JsonDocument.Parse(zip.GetEntry("diagnostics.json").Open());Check(!info.RootElement.GetProperty("saveFilesIncluded").GetBoolean(),"Logs-only metadata truthful");
}
string save=Path.Combine(folder,"committed.bnsm-save.zip");
using(var zip=ZipFile.Open(save,ZipArchiveMode.Create)){using var w=new StreamWriter(zip.CreateEntry("players.json").Open());w.Write("{\"committed\":true}");}
byte[] baseline=File.ReadAllBytes(save);
string combined=LocalMobileSupportArchive.Create(folder,client,server,state,save,"{\"saveFilesIncluded\":true}");
using(var zip=ZipFile.OpenRead(combined)){
 using var bytes=new MemoryStream();zip.GetEntry("save.bnsm-save.zip").Open().CopyTo(bytes);
 Check(baseline.SequenceEqual(bytes.ToArray()),"Committed original archive embedded byte for byte");
 Check(zip.GetEntry("说明.txt")!=null,"Restore instructions included");
}
File.WriteAllText(Path.Combine(client,"client.1.log"),new string('x',4*1024*1024));
using(var writer=new FileStream(Path.Combine(client,"client.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){
 string concurrent=LocalMobileSupportArchive.Create(folder,client,server,state,null,"{}");
 using var zip=ZipFile.OpenRead(concurrent);Check(zip.GetEntry("logs/client/client.1.log").Length==3*1024*1024,"Rotated logs bounded under active writer");
}
bool failed=false;try{LocalMobileSupportArchive.Create(folder,client,server,state,Path.Combine(folder,"missing.zip"),"{}");}catch(FileNotFoundException){failed=true;}
Check(failed&&!Directory.GetFiles(folder,"*.partial").Any(),"Failed export leaves no incomplete delivery");
Check(baseline.SequenceEqual(File.ReadAllBytes(save)),"Source save stays unchanged");
var report=new{success=true,checks,folder,nativeCompiled=false,deviceVerified=false};
File.WriteAllText(Path.Combine(root,"Verification/ios-support-contract.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("iOS portable support archive: "+checks.Count+" checks passed");
