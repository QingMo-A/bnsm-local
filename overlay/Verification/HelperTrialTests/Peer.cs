using System.Net.Sockets;
using System.Text.Json;
sealed class Peer(string root):IDisposable
{
    public byte[] LastPayload {get;private set;}=[];
    private readonly TcpClient gameSocket=new();
    private readonly WireCodec codec=new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root,"LocalServer/data/packet-keys.json")))!);
    private SessionKeyInfo key=new();
    private async Task<(ushort Command,byte[] Payload)> Ask(TcpClient client,ushort command,object value)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.GetStream().WriteAsync(codec.Encode(command,WireCodec.Serialize(value),true),timeout.Token);
        var decoded=codec.Decode((await WireCodec.ReadFrame(client.GetStream(),timeout.Token))!,true);
        return(decoded.Command,decoded.Payload);
    }
    public async Task Login(LocalRuntime server,string account)
    {
        using var socket=new TcpClient();await socket.ConnectAsync("127.0.0.1",server.LoginPort);
        await Ask(socket,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,new LsLoginRequest{userID=account,userPW="local",osType=DeviceOSType.DEVICE_PC});
        var r=await Ask(socket,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,new LsGameStartRequest{serverId=1,bAgreement=true});
        var started=(LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk),r.Payload)!;key=new(){uid=started.uid,loginSessionKey=started.loginSessionKey};
        await gameSocket.ConnectAsync("127.0.0.1",server.GamePort);
    }
    public async Task<T> Request<T>(GameServerCommand command,object request,bool retry=false,bool success=true)
    {
        if(!retry)key.sequenceNo++;(request.GetType().GetField("keyInfo")??request.GetType().GetField("KeyInfo"))?.SetValue(request,key);
        var r=await Ask(gameSocket,(ushort)command,request);
        LastPayload=r.Payload;
        if(((GameServerCommand)r.Command).ToString()!=command.ToString().Replace("_REQUEST",success?"_OK":"_FAIL"))throw new Exception("Unexpected reply: "+(GameServerCommand)r.Command+" "+System.Text.Encoding.UTF8.GetString(r.Payload));
        if(success && typeof(T)==typeof(GsGetDebugPromotionInfoOk)) {
            var merged=(GsGetDebugPromotionInfoOk)WireCodec.Deserialize(typeof(GsGetDebugPromotionInfoOk),r.Payload)!;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(merged.currentCount<merged.totalCount) {
                var frame=codec.Decode((await WireCodec.ReadFrame(gameSocket.GetStream(),timeout.Token))!,true);
                if(frame.Command!=r.Command)throw new InvalidDataException("Unexpected multipart activity command");
                var next=(GsGetDebugPromotionInfoOk)WireCodec.Deserialize(typeof(GsGetDebugPromotionInfoOk),frame.Payload)!;
                if(next.currentCount==0||next.totalCount!=merged.totalCount)throw new InvalidDataException("Invalid activity part");
                merged.currentCount+=next.currentCount;merged.promotionDataList.AddRange(next.promotionDataList);
            }
            return (T)(object)merged;
        }
        return typeof(T)==typeof(object)?(T)new object():(T)WireCodec.Deserialize(typeof(T),r.Payload)!;
    }
    public void Dispose()=>gameSocket.Dispose();
}
