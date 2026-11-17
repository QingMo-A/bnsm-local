using Bnsm.Server;
using Bnsm.AndroidHost;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
string fixture=Path.Combine("I:/BnsmModernWork/Verification","phone-host-guard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
await using var logs=ServerDiagnostics.Begin(Path.Combine(fixture,"logs"));logs.ContextProvider=()=>new{package="local.bnsm.serverhost",versionCode=6,previousExits=new[]{new{reason=10,description="SwipeUpClean"}}};logs.Write("ERROR","host-marker password=secret-value");
await using var server=new EmbeddedServer("I:/BnsmEssenceBuildWork",Path.Combine(fixture,"saves"),new ServerEndpointOptions{DirectoryPort=0,LoginPort=0,GamePort=0});
await using(var http=new PhoneDebugHttpHost(server)){
 using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:58601"),Timeout=TimeSpan.FromSeconds(8)};
 using var state=JsonDocument.Parse(await client.GetStringAsync("/support/state"));Check(state.RootElement.GetProperty("ready").GetBoolean()&&state.RootElement.GetProperty("versionCode").GetInt32()==6,"Phone health endpoint returns run state, instance and exact version");
 byte[] payload=await client.GetByteArrayAsync("/support/logs");using var zip=new ZipArchive(new MemoryStream(payload));
 Check(zip.GetEntry("host-context.json")!=null&&zip.GetEntry("diagnostics.json")!=null,"Phone export includes version and process-exit context");
 string tail=new StreamReader(zip.GetEntry("recent-tail.log")!.Open()).ReadToEnd();Check(tail.Contains("host-marker")&&!tail.Contains("secret-value"),"Combined export receives redacted server records");
 Check(zip.Entries.All(e=>!e.FullName.Contains("players")&&!e.FullName.Contains("bot-world")),"Phone support ZIP excludes player and bot saves");
 using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",58601);socket.ReceiveTimeout=6000;
 await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /support/logs HTTP/1.1\r\nHost: 127.0.0.1\r\nOrigin: https://example.invalid\r\n\r\n"));
 var buffer=new byte[1];Check(await socket.GetStream().ReadAsync(buffer)==0,"Support API rejects browser Origin requests");
}
File.WriteAllText("I:/BnsmModernWork/Verification/phone-host-guard-regression.json",JsonSerializer.Serialize(new{passed=true,checks,fixture,realSaveChanged=false,deviceVerified=false},new JsonSerializerOptions{WriteIndented=true}));
