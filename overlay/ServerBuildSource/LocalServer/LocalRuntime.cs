using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Reflection;
using System.Diagnostics;

internal sealed class LocalRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<TcpListener> listeners = [];
    private readonly List<Task> acceptLoops = [];
    private Task? lifeLoop;
    private readonly ConcurrentDictionary<int, Task> clients = new();
    private readonly WireCodec codec;
    public readonly GameCatalog Catalog;
    public readonly PlayerStore Store;
    public readonly ProtocolRouter Router;
    public int DirectoryPort { get; }
    public int LoginPort { get; }
    public int GamePort { get; }
    private int clientNumber;
    private int activeGameConnections;
    private long lastGameRequestTicks;
    public int ActiveGameConnections=>Volatile.Read(ref activeGameConnections);
    public DateTime? LastGameRequestUtc=>Interlocked.Read(ref lastGameRequestTicks) is >0 and var ticks?new DateTime(ticks,DateTimeKind.Utc):null;
    private readonly bool traceRequests = Environment.GetEnvironmentVariable("BNSM_TRACE_REQUESTS") == "1";
    public LocalRuntime(string root, string saveDirectory, int directoryPort = 10101, int loginPort = 10102, int gamePort = 10103,bool? repairPlaceholderCards=null, Bnsm.Server.ServerEndpointOptions? endpoints=null,bool enableOriginalCardPrototype=false)
    {
        endpoints ??= new() { DirectoryPort=directoryPort, LoginPort=loginPort, GamePort=gamePort };
        endpoints.Validate();
        codec = new(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        Catalog = new(root,repairPlaceholderCards,enableOriginalCardPrototype); Store = new(saveDirectory, Catalog);
        if (Store.BackfillMissingStageStars() > 0)
            System.Console.WriteLine("Stage star records backfilled for profiles that only had clear progress.");
        int Bind(int port)
        {
            var listener = new TcpListener(endpoints.ListenAddress, port);
            try { listener.Start(); } catch { foreach (var old in listeners) old.Stop(); throw; }
            listeners.Add(listener); return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        DirectoryPort = Bind(endpoints.DirectoryPort); LoginPort = Bind(endpoints.LoginPort); GamePort = Bind(endpoints.GamePort);
        Router = new(root, Store, Catalog, LoginPort, GamePort, endpoints.AdvertisedAddress);
        for (int i = 0; i < listeners.Count; i++) acceptLoops.Add(Accept(listeners[i], (ServiceKind)i));
        lifeLoop = Task.Run(BotLifeLoop);
        System.Console.WriteLine($"Server endpoints ({endpoints.ListenAddress}): directory={DirectoryPort}, login={LoginPort}, game={GamePort}");
    }
    /// <summary>
    /// 居民日活的驱动。服务端原本是纯请求驱动，世界聊天只在玩家轮询时才推进；
    /// 人机要"活在服务端"，得有自己的时间。
    /// 5 秒轮询一次，是否真的行动由 Advance 内部的时间门槛决定——轮询间隔不能大于日活间隔，
    /// 否则实际节奏会被轮询粒度卡住。
    /// 世界频道也挂在这里：频道不该只在玩家打开面板时才热闹，关着也该照常有人说话。
    /// </summary>
    private async Task BotLifeLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            try { Router.BotLifeTick(); Router.AmbientTick(); Router.SettlementTick(); Router.GuildBotTick(); }
            catch (Exception ex) { System.Console.WriteLine("[bot-life] " + ex.Message); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }
    private async Task Accept(TcpListener listener, ServiceKind kind)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try {client=await listener.AcceptTcpClientAsync(stop.Token);}
                catch(SocketException error) when(!stop.IsCancellationRequested) {System.Console.WriteLine($"[{kind}] listener retry: {error.SocketErrorCode}");await Task.Delay(250,stop.Token);continue;}
                int id = Interlocked.Increment(ref clientNumber);
                var task = Task.Run(() => HandleClient(client, kind, id));
                clients[id] = task;
                _ = task.ContinueWith(_ => clients.TryRemove(id, out var removed), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
    private async Task HandleClient(TcpClient client, ServiceKind kind, int id)
    {
        using (client)
        {
            client.NoDelay = true;
            var connection = new ConnectionState();
            bool encrypted = kind != ServiceKind.Directory;
            if(kind==ServiceKind.Game){Interlocked.Increment(ref activeGameConnections);System.Console.WriteLine($"[Game #{id}] connected");}
            try
            {
                using var stream = client.GetStream();
                while (!stop.IsCancellationRequested)
                {
                    var frame = await WireCodec.ReadFrame(stream, stop.Token);
                    if (frame == null) break;
                    var request = codec.Decode(frame, encrypted);
                    if (request.Sequence != WireCodec.Crc(request.Payload)) throw new InvalidDataException("Request CRC mismatch");
                    if(kind==ServiceKind.Game)Interlocked.Exchange(ref lastGameRequestTicks,DateTime.UtcNow.Ticks);
                    var timer = Stopwatch.StartNew();
                    var reply = Router.Handle(kind, request.Command, request.Payload, connection);
                    int bytes=0,parts=0;
                    var payloads = SerializeReplyParts(reply.Message).ToArray();
                    foreach(var payload in payloads) {
                        byte[] response = codec.Encode(reply.Command, payload, encrypted, ++connection.Sequence);
                        await stream.WriteAsync(response, stop.Token);bytes+=response.Length;parts++;
                    }
                    if (traceRequests || timer.ElapsedMilliseconds >= 250) System.Console.WriteLine($"[{kind} #{id}] {request.Command} -> {reply.Name} ({bytes} bytes, {parts} parts, {timer.Elapsed.TotalMilliseconds:F1} ms)");
                    if(reply.Message is FailPacket failure)System.Console.WriteLine($"[{kind} #{id}] {reply.Name}: error={failure.errorCode}, {failure.message}");
                }
            }
            catch (Exception ex)
            {
                if (!stop.IsCancellationRequested) System.Console.WriteLine($"[{kind} #{id}] closed: {ex}");
            }
            finally {if(kind==ServiceKind.Game){Interlocked.Decrement(ref activeGameConnections);System.Console.WriteLine($"[Game #{id}] disconnected");}}
        }
    }
    internal static IEnumerable<byte[]> SerializeReplyParts(object? message)
    {
        foreach (var part in SplitReply(message))
            foreach (var payload in FitReply(part)) yield return payload;
    }
    private static IEnumerable<byte[]> FitReply(object? part)
    {
        byte[] payload = WireCodec.Serialize(part);
        if (payload.Length + WireCodec.HeaderSize <= WireCodec.MaxPacketSize) { yield return payload; yield break; }
        IEnumerable<object> smaller;
        if (part is GsGetUserCardListOk cards && cards.userCardInfoList.Count > 1)
            smaller=cards.userCardInfoList.Chunk((cards.userCardInfoList.Count+1)/2).Select(batch=> { var p=new GsGetUserCardListOk {totalDataCount=cards.totalDataCount,dataCount=(uint)batch.Length};p.userCardInfoList.AddRange(batch);return (object)p; });
        else if (part is GsGetUserItemListOk items && items.UserItemList.Count > 1)
            smaller=items.UserItemList.Chunk((items.UserItemList.Count+1)/2).Select(batch=> { var p=new GsGetUserItemListOk {totalDataCount=items.totalDataCount,dataCount=(uint)batch.Length};p.UserItemList.AddRange(batch);return (object)p; });
        else if (part is GsGetRewardInfoOk mail && mail.rewardList.Count > 1)
            smaller=MailReplyParts(mail,(mail.rewardList.Count+1)/2);
        else if(part is GsGetDebugPromotionInfoOk events && events.promotionDataList.Count > 1)
            smaller=events.promotionDataList.Chunk((events.promotionDataList.Count+1)/2).Select(batch=>{var p=new GsGetDebugPromotionInfoOk{totalCount=events.totalCount,currentCount=(uint)batch.Length};p.promotionDataList.AddRange(batch);return (object)p;});
        else throw new InvalidDataException("Reply exceeds client frame capacity: " + part?.GetType().Name);
        foreach (var child in smaller) foreach (var bytes in FitReply(child)) yield return bytes;
    }
    private static IEnumerable<GsGetRewardInfoOk> MailReplyParts(GsGetRewardInfoOk mail,int size)
    {
        foreach(var batch in mail.rewardList.Chunk(size)){
            var part=new GsGetRewardInfoOk{totalDataCount=mail.totalDataCount,dataCount=(uint)batch.Length};
            part.rewardList.AddRange(batch);yield return part;
        }
    }
    private static IEnumerable<object?> SplitReply(object? message)
    {
        // Original coroutine accumulates dataCount until totalDataCount, with no
        // extra request. Keep every encrypted frame within the original 8192 cap.
        if(message is GsGetUserCardListOk cards && cards.userCardInfoList.Count>0) {
            foreach(var batch in cards.userCardInfoList.Chunk(64)) {
                var part=new GsGetUserCardListOk {totalDataCount=cards.totalDataCount,dataCount=(uint)batch.Length};
                part.userCardInfoList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetUserItemListOk items && items.UserItemList.Count>0) {
            foreach(var batch in items.UserItemList.Chunk(64)) {
                var part=new GsGetUserItemListOk {totalDataCount=items.totalDataCount,dataCount=(uint)batch.Length};
                part.UserItemList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetRewardInfoOk mail && mail.rewardList.Count>0) {
            foreach(var part in MailReplyParts(mail,12))yield return part;
        } else if(message is GsGetWorldBossTopRankerOk bosses && bosses.topRankerList.Count>0) {
            foreach(var batch in bosses.topRankerList.Chunk(8)){
                var part=new GsGetWorldBossTopRankerOk{bossId=bosses.bossId,totalCount=bosses.totalCount,currentCount=(uint)batch.Length,userRankData=bosses.userRankData};part.topRankerList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetRankingBoardTopRankerOk ranks && ranks.topRankerList.Count>0) {
            foreach(var batch in ranks.topRankerList.Chunk(16)){
                var part=new GsGetRankingBoardTopRankerOk{totalCount=ranks.totalCount,currentCount=(uint)batch.Length,userRankData=ranks.userRankData};part.topRankerList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetFriendListOk friends && friends.friendList.Count>0) {
            foreach(var batch in friends.friendList.Chunk(16)){
                var part=new GsGetFriendListOk{totalCount=friends.totalCount,currentCount=(uint)batch.Length,remainHaveDrinkCount=friends.remainHaveDrinkCount,remainReceiveDrinkCount=friends.remainReceiveDrinkCount,refundDrinkCount=friends.refundDrinkCount};
                part.friendList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetInviteFriendListOk pending && pending.inviteFriendList.Count>0) {
            foreach(var batch in pending.inviteFriendList.Chunk(128)){
                var part=new GsGetInviteFriendListOk{totalCount=pending.totalCount,currentCount=(uint)batch.Length};part.inviteFriendList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetMyGuildUserInfoListOk members && members.guildUserInfoList.Count>0) {
            foreach(var batch in members.guildUserInfoList.Chunk(16)){
                var part=new GsGetMyGuildUserInfoListOk{totalDataCount=members.totalDataCount,dataCount=(uint)batch.Length};
                part.guildUserInfoList.AddRange(batch);yield return part;
            }
        } else if(message is GsGetGuildInvitedListOk invitations && invitations.guildInfoList.Count>0) {
            foreach(var batch in invitations.guildInfoList.Chunk(16)){
                var part=new GsGetGuildInvitedListOk{totalDataCount=invitations.totalDataCount,dataCount=(uint)batch.Length};
                part.guildInfoList.AddRange(batch);yield return part;
            }
        } else yield return message;
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); foreach (var listener in listeners) listener.Stop();
        await Task.WhenAll(acceptLoops); await Task.WhenAll(clients.Values);
        if (lifeLoop != null) await lifeLoop;
        Store.Save(); stop.Dispose();
    }
}
