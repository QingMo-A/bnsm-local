using Bnsm.Server;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

internal static class EmbeddedHostTest
{
    public static async Task Run(string root)
    {
        string save = Path.Combine(root, "verification/embedded-host", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label)
        {
            bool failed = false;
            try { action(); } catch (ArgumentException) { failed = true; } catch (NotSupportedException) { failed = true; }
            Check(failed, label);
        }
        var local = new GameConnectionSettings { ExternalHost = "unused.invalid" }.Resolve();
        Check(local.Host == "127.0.0.1" && local.Port == 10101, "Default connection is local even when a stale external hostname exists");
        Reject(() => new GameConnectionSettings { Mode = "external", ExternalHost = "192.0.2.10" }.Resolve(), "External mode is disabled unless the host explicitly enables it");
        var external = new GameConnectionSettings { Mode = "external", ExternalHost = "game.example", ExternalDirectoryPort = 12345 }.Resolve(externalEnabled: true);
        Check(external.Host == "game.example" && external.Port == 12345, "Future external adapter accepts a separate directory endpoint");
        Reject(() => new GameConnectionSettings { Mode = "unknown" }.Resolve(), "Unknown mode is rejected");
        Reject(() => new GameConnectionSettings().Resolve(0), "Unusable client port is rejected");
        Reject(() => new GameConnectionSettings { Mode = "external", ExternalHost = "http://localhost/path" }.Resolve(externalEnabled: true), "URL is rejected where a host is required");
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        var options = new ServerEndpointOptions { DirectoryPort = 0, LoginPort = 0, GamePort = 0, AdvertisedAddress = IPAddress.Parse("192.0.2.42") };
        uint uid;
        int oldPort;
        async Task<T> Ask<T>(int port, ushort request, ushort expected, object message, bool encrypted)
        {
            using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", port);
            await client.GetStream().WriteAsync(codec.Encode(request, WireCodec.Serialize(message), encrypted));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = codec.Decode((await WireCodec.ReadFrame(client.GetStream(), timeout.Token))!, encrypted);
            Check(response.Command == expected, "Original command response " + expected);
            return (T)WireCodec.Deserialize(typeof(T), response.Payload)!;
        }
        await using (var host = new EmbeddedServer(root, save, options))
        {
            oldPort = host.DirectoryPort;
            var directory = await Ask<DsGetServerListOk>(host.DirectoryPort, (ushort)DirServerCommand.DS_GET_SERVER_LIST_REQUEST, (ushort)DirServerCommand.DS_GET_SERVER_LIST_OK, new DsGetServerListRequest { openId = "host-test" }, false);
            Check(directory.info.loginServerAddress == "192.0.2.42" && directory.info.loginServerVirtualPort == host.LoginPort, "Directory redirect uses advertised address and actual bound login port");
            // Login and game start must share the same TCP connection.
            using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", host.LoginPort);
            async Task<byte[]> Login(LoginServerCommand command, object message)
            {
                await login.GetStream().WriteAsync(codec.Encode((ushort)command, WireCodec.Serialize(message), true));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = codec.Decode((await WireCodec.ReadFrame(login.GetStream(), timeout.Token))!, true);
                Check(((LoginServerCommand)response.Command).ToString() == command.ToString().Replace("_REQUEST", "_OK"), "External-ready login response " + command);
                return response.Payload;
            }
            await Login(LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = "host-test", userPW = "local", osType = (DeviceOSType)2 });
            var active = (LsGetActiveGameServerIpOk)WireCodec.Deserialize(typeof(LsGetActiveGameServerIpOk), await Login(LoginServerCommand.LS_GET_ACTIVE_GAME_SERVER_IP_REQUEST, new LsGetActiveGameServerIpRequest { serverId = 1 }))!;
            Check(active.gameServerAddress == "192.0.2.42" && active.gameServerIP == 0x2A0200C0u && active.gameServerVirtualPort == host.GamePort, "Active-game endpoint API follows the same advertised endpoint");
            var account = (LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk), await Login(LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true }))!;
            uid = account.uid;
            Check(account.gameServerAddress == "192.0.2.42" && account.gameServerIP == 0x2A0200C0u && account.gameServerVirtualPort == host.GamePort, "Game redirect carries matching original little-endian IPv4 and address");
            host.Checkpoint();
            Check(File.Exists(Path.Combine(save, "players.json")) && File.Exists(Path.Combine(save, "save-format.json")), "Embedded host writes the existing schema and player files");
        }
        using (var listener = new TcpListener(IPAddress.Loopback, oldPort)) { listener.Start(); Check(true, "Stopping embedded host releases its listening port"); }
        await using (var host = new EmbeddedServer(root, save, new() { DirectoryPort = 0, LoginPort = 0, GamePort = 0 }))
        {
            var dir = await Ask<DsGetServerListOk>(host.DirectoryPort, 1, 2, new DsGetServerListRequest { openId = "host-test" }, false);
            Check(dir.info.loginServerAddress == "127.0.0.1", "Fresh host options restore local advertisement");
            Check(new PlayerStore(save, new GameCatalog(root)).Find(uid) != null, "Android-compatible embedded lifecycle preserves existing player data");
        }
        Check(!typeof(EmbeddedServer).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)), "Shared business core has no ASP.NET host dependency");
        string report = Path.Combine(root, "evidence/embedded-host-test.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: " + checks.Count + " embedded host checks.");
    }
}
