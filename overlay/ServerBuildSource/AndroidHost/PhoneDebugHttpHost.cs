using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bnsm.Server;

namespace Bnsm.AndroidHost;

/// <summary>Bounded loopback HTTP adapter for the game's existing native debug panel.</summary>
internal sealed class PhoneDebugHttpHost : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 58601);
    private readonly CancellationTokenSource stop = new();
    private readonly EmbeddedServer server;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int,Task> clients = new();
    private readonly Task accepting;
    private int nextClient;
    public PhoneDebugHttpHost(EmbeddedServer server)
    {
        this.server=server;
        listener.Start(4);
        accepting=Accept();
    }
    private async Task Accept()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var client=await listener.AcceptTcpClientAsync(stop.Token);
                int id=Interlocked.Increment(ref nextClient);
                clients[id]=Serve(client);
                _=clients[id].ContinueWith(completed=>{clients.TryRemove(id,out _);},TaskScheduler.Default);
            }
        }
        catch(OperationCanceledException) { }
        catch(SocketException) when(stop.IsCancellationRequested) { }
    }
    private async Task Serve(TcpClient client)
    {
        using(client)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var token=timeout.Token;
            try
            {
                using var stream=client.GetStream();
                if(client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))return;
                int total=0;
                async Task<string> Line()
                {
                    var bytes=new List<byte>();var one=new byte[1];
                    while(true)
                    {
                        if(++total>8192)throw new IOException("HTTP header too large");
                        if(await stream.ReadAsync(one,token)==0)throw new EndOfStreamException();
                        if(one[0]==10)break;
                        if(one[0]!=13)bytes.Add(one[0]);
                    }
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }
                var first=(await Line()).Split(' ');
                if(first.Length!=3 || first[1].Length>2048 || !first[1].StartsWith('/'))return;
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                for(string line=await Line();line.Length!=0;line=await Line())
                {
                    int colon=line.IndexOf(':');
                    if(colon<=0 || !headers.TryAdd(line[..colon],line[(colon+1)..].Trim()))return;
                }
                // Native Unity requests have no Origin. Do not expose a browser/CORS bridge.
                if(headers.ContainsKey("Origin") || headers.ContainsKey("Transfer-Encoding"))return;
                int length=0;
                if(headers.TryGetValue("Content-Length",out var value) && (!int.TryParse(value,out length)||length<0||length>16384))return;
                var uri=new Uri("http://127.0.0.1"+first[1]);
                bool known=first[0]=="GET" && uri.AbsolutePath is "/support/state" or "/support/logs" or "/debug/state" or "/debug/players" or "/debug/bots" or "/debug/bots/opponents"
                    || first[0]=="POST" && uri.AbsolutePath is "/debug/options" or "/debug/challenges/restore" or "/debug/level" or "/debug/card-exp" or "/debug/vip" or "/debug/currency" or "/debug/skill-point" or "/debug/bots/generate" or "/debug/bots/configure";
                if(!known)return;
                if(first[0]=="POST" && (!headers.TryGetValue("Content-Type",out var type)||!type.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)))return;
                var body=new byte[length];await stream.ReadExactlyAsync(body,token);
                uint? uid=null;
                foreach(var pair in uri.Query.TrimStart('?').Split('&'))
                    if(pair.StartsWith("uid=") && uint.TryParse(pair[4..],out uint parsed))uid=parsed;
                byte[] json;
                string contentType="application/json; charset=utf-8";
                if(uri.AbsolutePath=="/support/state")json=JsonSerializer.SerializeToUtf8Bytes(PhoneSupportInfo.Snapshot(Android.App.Application.Context));
                else if(uri.AbsolutePath=="/support/logs"){json=ServerDiagnostics.Current?.Export()??throw new IOException("Logs unavailable");contentType="application/zip";}
                else json=JsonSerializer.SerializeToUtf8Bytes(server.HandleDebug(first[0],uri.AbsolutePath,uid,Encoding.UTF8.GetString(body)));
                byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: "+contentType+"\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: "+json.Length+"\r\n\r\n");
                await stream.WriteAsync(response,token);await stream.WriteAsync(json,token);
            }
            catch(OperationCanceledException) { }
            catch(IOException) { }
            catch(Exception error) { Android.Util.Log.Error("BnsmLocalHost","Debug request: "+error.GetType().Name); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();listener.Stop();
        await accepting;
        await Task.WhenAll(clients.Values.ToArray());
        stop.Dispose();
    }
}
