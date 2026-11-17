using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
var checks=new List<string>();
void Check(bool result,string name){if(!result)throw new Exception(name);checks.Add(name);}
string fixture=Path.Combine("I:/BnsmModernWork/Verification","phone-guard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
string folder=Path.Combine(fixture,"logs");
using(var logs=new LocalDiagnosticLog(folder)){
 logs.Write("ERROR","startup-marker loginToken=hidden123 sessionKey=secret456 password=pass789 openId=person012");
 await Task.Delay(200);
 Check(logs.Tail().Contains("startup-marker")&&!logs.Tail().Contains("hidden123")&&!logs.Tail().Contains("secret456")&&!logs.Tail().Contains("person012"),"Client credentials are redacted from persistent and recent logs");
 Check(!LocalDiagnosticLog.Redact("packet buffer=[123456789abc]").Contains("123456789abc"),"Packet hex dumps are omitted");
 for(int i=0;i<1600;i++){logs.Write("INFO",new string('r',4096));if(i%30==0)await Task.Delay(10);}
}
Check(Directory.GetFiles(folder,"*.log").Length is >=2 and <=4,"Client logs rotate within four retained files");
Check(Directory.GetFiles(folder,"*.log").All(p=>new FileInfo(p).Length<=LocalDiagnosticLog.FileLimit),"Every client log stays within 1 MiB");
using(var again=new LocalDiagnosticLog(folder)){again.Write("INFO","after-restart");}
Check(File.ReadAllText(Path.Combine(folder,"client.log")).Contains("after-restart"),"Restart appends to retained logs instead of erasing them");
string blocked=Path.Combine(fixture,"blocked");File.WriteAllText(blocked,"file");
using(var logs=new LocalDiagnosticLog(blocked)){logs.Write("ERROR","disk-failure-marker");await Task.Delay(100);Check(logs.DiskFailed&&logs.Tail().Contains("disk-failure-marker"),"Unavailable storage preserves recent in-memory diagnostics");}
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
try{
 var client=new SocketBase(false){ipAddress="127.0.0.1",port=port,encryptMode=false};int connected=0;client.connectCompelete=()=>connected++;
 for(int i=0;i<300;i++){
  var accepted=listener.AcceptTcpClientAsync();CheckConnect(client.ConnectAsync());
  using var peer=await accepted.WaitAsync(TimeSpan.FromSeconds(3));
  var clock=Stopwatch.StartNew();while(!client.Connected()&&clock.ElapsedMilliseconds<1500){client.MsgPump();await Task.Delay(1);}
  if(!client.Connected())throw new Exception("loopback connect hung at "+i);
  client.Disconnect();
 }
 Check(connected==300,"300 rapid loopback connects and reconnects complete without lost callbacks");
 client.ConnectAsync();using var waiting=await listener.AcceptTcpClientAsync();
 var field=typeof(SocketBase).GetField("asyncConnect",BindingFlags.Instance|BindingFlags.NonPublic);
 var time=typeof(SocketBase).GetField("connectStarted",BindingFlags.Instance|BindingFlags.NonPublic);
 field.SetValue(client,new NeverCompletes());time.SetValue(client,Stopwatch.GetTimestamp()-Stopwatch.Frequency*10);
 int failed=0;client.connectFail=()=>failed++;client.MsgPump();
 Check(failed==1&&!client.Connecting(),"A stalled connect reaches the monotonic timeout and failure callback");
 client.Disconnect();
}finally{listener.Stop();}
File.WriteAllText("I:/BnsmModernWork/Verification/phone-guard-regression.json",JsonSerializer.Serialize(new{passed=true,checks,fixture,realSaveChanged=false,deviceVerified=false},new JsonSerializerOptions{WriteIndented=true}));
void CheckConnect(bool value){if(!value)throw new Exception("ConnectAsync returned false");}
sealed class NeverCompletes:IAsyncResult{public object AsyncState=>null;public WaitHandle AsyncWaitHandle=>throw new NotSupportedException();public bool CompletedSynchronously=>false;public bool IsCompleted=>false;}
