using Bnsm.Server;
using System.Net.Sockets;
using System.Text.Json;

namespace Bnsm.AndroidHost;

/// <summary>Real sockets in the phone process; test-only accounts never use the normal mobile profile.</summary>
internal static class PhoneProtocolProbe
{
    private sealed record Snapshot(uint Uid, uint Gold, uint Cash, uint Level, string Intro, uint[] Cards);
    public static async Task<object> Run(string root, IEmbeddedGameServer server, string run, Action<string> log)
    {
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidDataException(message);
            checks.Add(message); log("✓ " + message);
        }
        Check(Environment.Is64BitProcess, "服务端运行在手机 64 位进程");
        Check(server.SaveDirectory.Replace('\\', '/').EndsWith("/verification/" + run + "/saves"), "协议自测只操作独立测试档");
        Check(new GameConnectionSettings().Resolve().Host == "127.0.0.1", "客户端连接策略默认只指向手机回环");
        bool rejected = false;
        try { new GameConnectionSettings { Mode = "external", ExternalHost = "192.0.2.10" }.Resolve(); }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "预留外服配置在当前手机策略下被拒绝");
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        async Task<(ushort Command, byte[] Payload)> Ask(TcpClient client, ushort command, object message, bool encrypted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(message), encrypted), timeout.Token);
            var frame = await WireCodec.ReadFrame(client.GetStream(), timeout.Token) ?? throw new EndOfStreamException();
            var response = codec.Decode(frame, encrypted);
            return (response.Command, response.Payload);
        }
        T Read<T>(byte[] value) => (T)WireCodec.Deserialize(typeof(T), value)!;
        using (var directory = new TcpClient())
        {
            await directory.ConnectAsync("127.0.0.1", server.DirectoryPort);
            var response = await Ask(directory, (ushort)DirServerCommand.DS_GET_SERVER_LIST_REQUEST, new DsGetServerListRequest { openId = "phone-local-probe" }, false);
            var list = Read<DsGetServerListOk>(response.Payload);
            Check(response.Command == (ushort)DirServerCommand.DS_GET_SERVER_LIST_OK && list.info.loginServerAddress == "127.0.0.1" && list.info.loginServerVirtualPort == server.LoginPort, "手机目录服下发手机本机登录地址");
        }
        async Task<Snapshot> Session(bool mutate)
        {
            using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
            var logged = await Ask(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = "phone-local-probe", userPW = "local", deviceToken = "isolated", osType = (DeviceOSType)2 }, true);
            Check(logged.Command == (ushort)LoginServerCommand.LS_LOGIN_OK, "手机内加密登录协议通过");
            var start = await Ask(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true }, true);
            var account = Read<LsGameStartOk>(start.Payload);
            Check(start.Command == (ushort)LoginServerCommand.LS_GAME_START_OK && account.gameServerAddress == "127.0.0.1" && account.gameServerIP == 0x0100007Fu && account.gameServerVirtualPort == server.GamePort, "登录服下发手机游戏服地址与会话");
            using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
            var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
            async Task<byte[]> Game(GameServerCommand command, object request)
            {
                key.sequenceNo++;
                var response = await Ask(game, (ushort)command, request, true);
                Check(((GameServerCommand)response.Command).ToString() == command.ToString().Replace("_REQUEST", "_OK"), command.ToString());
                return response.Payload;
            }
            var info = Read<GsGetUserGameInfoOk>(await Game(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key }));
            var cards = Read<GsGetUserCardListOk>(await Game(GameServerCommand.GS_GET_USER_CARD_LIST_REQUEST, new GsGetUserCardListRquest { keyInfo = key }));
            Check(cards.dataCount == cards.totalDataCount && cards.userCardInfoList.Count == 5, "手机加载原始表与五张初始卡");
            string intro = info.namecardInfo.introMsg ?? "";
            if (mutate)
            {
                await Game(GameServerCommand.GS_UPDATE_USER_INTRO_MESSAGE_REQUEST, new GsUpdateUserIntroMessageRequest { keyInfo = key, introMsg = "phone-local-" + run });
                info = Read<GsGetUserGameInfoOk>(await Game(GameServerCommand.GS_GET_USER_GAME_INFO_REQUEST, new GsGetUserGameInfoRequest { keyInfo = key }));
                Check(info.namecardInfo.introMsg == "phone-local-" + run, "原协议写入测试签名并立即读回");
            }
            else Check(intro == "phone-local-" + run, "断线重登后测试签名仍存在");
            return new(account.uid, info.coinInfo.gold, info.coinInfo.cashPoint, info.namecardInfo.commonInfo.level, intro, cards.userCardInfoList.Select(c => c.cardID).ToArray());
        }
        var before = await Session(true);
        server.Checkpoint();
        var after = await Session(false);
        Check(before.Uid == after.Uid && before.Gold == after.Gold && before.Cash == after.Cash && before.Cards.SequenceEqual(after.Cards), "重连后账号、卡牌和钱包保持一致");
        Check(File.Exists(Path.Combine(server.SaveDirectory, "players.json")), "存档实际写入手机应用私有目录");
        using var format = JsonDocument.Parse(File.ReadAllText(Path.Combine(server.SaveDirectory, "save-format.json")));
        Check(format.RootElement.GetProperty("SchemaVersion").GetInt32() == EmbeddedServer.SaveSchema, "手机与 Windows 使用相同版本的存档结构");
        using var http=new HttpClient { BaseAddress=new Uri("http://127.0.0.1:58601"), Timeout=TimeSpan.FromSeconds(8) };
        async Task<JsonElement> Get(string path)
        {
            using var response=await http.GetAsync(path);
            response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        async Task<JsonElement> Post(string path,object body)
        {
            using var content=new StringContent(JsonSerializer.Serialize(body),System.Text.Encoding.UTF8,"application/json");
            using var response=await http.PostAsync(path,content);
            response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        uint uid=after.Uid;
        Check((await Get("/debug/players")).GetProperty("ok").GetBoolean(),"手机本地调试接口可读取账号列表");
        var debugState=await Get("/debug/state?uid="+uid);
        Check(debugState.GetProperty("ok").GetBoolean() && debugState.GetProperty("uid").GetUInt32()==uid,"调试台按当前账号读取状态");
        Check((await Post("/debug/level",new {uid,level=after.Level})).GetProperty("ok").GetBoolean(),"手机调试等级操作接入共用规则");
        Check((await Post("/debug/card-exp",new {uid,seq=1,exp=0})).GetProperty("ok").GetBoolean(),"手机调试卡牌经验操作可用");
        Check((await Post("/debug/vip",new {uid,level=0})).GetProperty("ok").GetBoolean(),"手机调试特权操作可用");
        Check(!(await Post("/debug/currency",new {uid,currency="cash",amount=0})).GetProperty("ok").GetBoolean(),"手机调试资源接口拒绝无效数量");
        Check((await Post("/debug/skill-point",new {uid})).GetProperty("ok").GetBoolean(),"手机调试武功点恢复可用");
        Check((await Post("/debug/options",new {uid,option=0,enabled=true})).GetProperty("ok").GetBoolean(),"手机调试规则可开启");
        Check((await Get("/debug/state?uid="+uid)).GetProperty("flags").GetUInt32()!=0,"手机调试规则实际写入状态");
        Check((await Post("/debug/options",new {uid,option=0,enabled=false})).GetProperty("ok").GetBoolean(),"手机调试规则可恢复关闭");
        Check((await Post("/debug/challenges/restore",new {uid,mode="all"})).GetProperty("ok").GetBoolean(),"手机调试挑战次数恢复可用");
        Check((await Get("/debug/state?uid="+uid)).GetProperty("flags").GetUInt32()==0,"手机调试测试未遗留规则开关");
        server.Checkpoint();
        return new { passed = true, scope = "Server and original encrypted protocol execute inside phone; no Unity gameplay", is64BitProcess = Environment.Is64BitProcess,
            serverProcessId = Android.OS.Process.MyPid(), androidApi = (int)Android.OS.Build.VERSION.SdkInt,
            localOnly = true, externalEnabled = false, needsPcServer = false, needsUsbReverse = false, unityGameplayVerified = false,
            saveDirectory = server.SaveDirectory, before, after, checks };
    }
}
