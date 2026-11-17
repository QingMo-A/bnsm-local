using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

internal static class PerformanceTest
{
    internal static async Task Fixture(string root, string output)
    {
        output=Path.GetFullPath(output);
        string allowed=Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture directory required");
        Directory.CreateDirectory(output);
        await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0)
        {
            var p=runtime.Store.GetOrCreate("local-player");var c=runtime.Catalog;
            p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);p.Gold=1000000000;p.CashPoint=1000000000;
            var item=c.Items.First(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.Extraction&&x.Id!=100073059);
            for(int i=0;i<600;i++)p.EquipmentItems.Add(new(){ItemId=(uint)item.Id,Sequence=p.NextEquipmentSequence++,RegisteredAt=ProtocolRouter.Now});
            foreach(var stack in c.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_INGREDIENT).Take(100))p.Inventory[(uint)stack.Id]=100;
            ulong day=(ProtocolRouter.Now+8*3600)/86400;p.AttendanceDays=1;p.AttendanceLastDay=day;p.AttendanceVipClaimedDay=day;p.ShortTermDays=1;p.ShortTermLastDay=day;
            runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
    private sealed class BaselineContext() : AssemblyLoadContext("BnsmPerformanceBaseline", true)
    {
        protected override Assembly? Load(AssemblyName name) => AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == name.Name && name.Name != "Bnsm.ServerCore");
    }
    internal static async Task Run(string root, string? baseline)
    {
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var c = new GameCatalog(root);
        string scope = Path.Combine(root, "verification/request-performance", Guid.NewGuid().ToString("N"));
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);
        async Task<object> Measure(string label, string? assemblyPath)
        {
            string save = Path.Combine(scope, label); Directory.CreateDirectory(save);
            var p = new LocalPlayer { Uid = 1001, Account = "performance-fixture", Level = 99,
                Exp = (uint)c.PlayerStates.First(x => x.Level == 99).Exp, Gold = 2_000_000_000, CashPoint = 2_000_000_000,
                CardIds = Enumerable.Repeat(20103u, 300).ToList(), Deck = [1, 2, 3, 4, 5],
                SkillPoint = 20, SkillPointStamp = ProtocolRouter.Now, Stamina = c.StaminaMax(99), StaminaStamp = ProtocolRouter.Now };
            for (ulong i = 1; i <= 300; i++) p.CardStates[i] = new();
            var materials = c.Items.Where(x => x.ItemType != BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT).Take(700);
            foreach (var item in materials) p.Inventory[(uint)item.Id] = 1000000;
            p.Inventory[210002001] = 1000000;
            var forge = c.Items.First(x => x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT && x.Step == 5 && x.EquipmentType == BscTable.EQUIPMENT_TYPE.EQUIPMENT_TYPE_NORMAL && x.CardId == 0 && x.Next1Id != 0);
            foreach (int id in forge.Material1Id) p.Inventory[(uint)id] = 1000000;
            var extract = c.Items.First(x => x.ItemType == BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT && x.Extraction);
            for (int i = 0; i < 6000; i++) p.EquipmentItems.Add(new() { ItemId = (uint)(i < 20 ? forge.Id : extract.Id), Sequence = p.NextEquipmentSequence++, RegisteredAt = ProtocolRouter.Now });
            p.ExtraFields = new() { ["futurePerformanceField"] = JsonSerializer.SerializeToElement(new { retained = true }) };
            File.WriteAllText(Path.Combine(save, "players.json"), JsonSerializer.Serialize(new[] { p }));
            LocalRuntime? optimized = null; BaselineContext? context = null; IAsyncDisposable host; int loginPort, gamePort;
            if (assemblyPath == null)
            {
                optimized = new(root, save, 0, 0, 0); host = optimized; loginPort = optimized.LoginPort; gamePort = optimized.GamePort;
            }
            else
            {
                context = new(); var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                Type type = assembly.GetType("LocalRuntime", true)!;
                object runtime = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [root, save, 0, 0, 0, null, null], null)!;
                host = (IAsyncDisposable)runtime;
                loginPort = (int)type.GetProperty("LoginPort")!.GetValue(runtime)!; gamePort = (int)type.GetProperty("GamePort")!.GetValue(runtime)!;
            }
            await using (host)
            {
                using var login = new TcpClient { NoDelay = true }; await login.ConnectAsync("127.0.0.1", loginPort);
                async Task<byte[]> SendLogin(LoginServerCommand cmd, object req)
                {
                    await login.GetStream().WriteAsync(codec.Encode((ushort)cmd, WireCodec.Serialize(req), true));
                    using var timeout = new CancellationTokenSource(15000);
                    var response = codec.Decode((await WireCodec.ReadFrame(login.GetStream(), timeout.Token))!, true);
                    Check(response.Command == (ushort)(cmd + 1), label + " login response " + cmd); return response.Payload;
                }
                await SendLogin(LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = p.Account, userPW = "local", osType = DeviceOSType.DEVICE_PC });
                var start = (LsGameStartOk)WireCodec.Deserialize(typeof(LsGameStartOk), await SendLogin(LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true }))!;
                var key = new SessionKeyInfo { uid = start.uid, loginSessionKey = start.loginSessionKey };
                using var socket = new TcpClient { NoDelay = true }; await socket.ConnectAsync("127.0.0.1", gamePort);
                var times = new Dictionary<string, List<double>>();
                async Task<byte[]> Request(string category, GameServerCommand cmd, object req, bool replay = false)
                {
                    if (!replay) key.sequenceNo++;
                    var timer = Stopwatch.StartNew(); await socket.GetStream().WriteAsync(codec.Encode((ushort)cmd, WireCodec.Serialize(req), true));
                    using var timeout = new CancellationTokenSource(15000);
                    var frame = await WireCodec.ReadFrame(socket.GetStream(), timeout.Token) ?? throw new IOException("closed: " + cmd);
                    var response = codec.Decode(frame, true);
                    Check(((GameServerCommand)response.Command).ToString() == cmd.ToString().Replace("_REQUEST", "_OK"), label + " " + cmd + " reply");
                    if (!times.TryGetValue(category, out var sample)) times[category] = sample = [];
                    sample.Add(timer.Elapsed.TotalMilliseconds); return response.Payload;
                }
                GsGetIngameShopSellingListRequest Page(int index)
                {
                    var request = new GsGetIngameShopSellingListRequest { keyInfo = key };
                    request.shopId.AddRange([91, FullShopCatalog.QueryMarker]);
                    request.shopId.AddRange(c.Items.Skip(index * 8).Take(8).Select(x => FullShopCatalog.GoodsBase + (uint)x.Id)); return request;
                }
                await Request("coldShop", GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST, Page(0));
                long? writesBefore = optimized?.Store.SaveWrites;
                for (int i = 0; i < 40; i++) await Request("shopPaging", GameServerCommand.GS_GET_INGAME_SHOP_SELLING_LIST_REQUEST, Page(i % 20));
                long? pageWrites = optimized == null ? null : optimized.Store.SaveWrites - writesBefore;
                if (optimized != null) Check(pageWrites == 0, "Paging large inventory never writes a player save");
                for (int i = 0; i < 12; i++)
                {
                    long before = optimized?.Store.SaveWrites ?? 0;
                    await Request("upgrade", GameServerCommand.GS_LEVELUP_CARD_REQUEST, new GsCardLevelUpRequest { keyInfo = key, targetCardSeq = 1, targetExp = 1 });
                    if (optimized != null) Check(optimized.Store.SaveWrites - before == 1, "Upgrade commits once before reply");
                    await Request("forge", GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST, new GsEquipmentCraftRequest { keyInfo = key, baseEquipmentSeq = p.EquipmentItems[i].Sequence, targetEquipmentId = (uint)forge.Next1Id });
                    await Request("disassemble", GameServerCommand.GS_EXTRACT_EQUIPMENT_ITEM_REQUEST, new LocalEquipmentRequest { keyInfo = key, equipmentItemSeq = p.EquipmentItems[100 + i].Sequence });
                }
                var replayRequest = new GsEquipmentCraftRequest { keyInfo = key, baseEquipmentSeq = p.EquipmentItems[19].Sequence, targetEquipmentId = (uint)forge.Next1Id };
                byte[] first = await Request("forge", GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST, replayRequest);
                byte[] repeated = await Request("replay", GameServerCommand.GS_EQUIPMENT_CRAFT_REQUEST, replayRequest, true);
                Check(first.SequenceEqual(repeated), label + " replays exact success without another charge");
                using var saved = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(save, "players.json")));
                Check(saved.RootElement[0].GetProperty("futurePerformanceField").GetProperty("retained").GetBoolean(), label + " preserves future save fields");
                Check(saved.RootElement[0].GetProperty("EquipmentItems").GetArrayLength() == 5988, label + " all twelve disassemblies durable at response");
                var report = times.ToDictionary(x => x.Key, x => { var sorted = x.Value.Order().ToArray(); return new { count = sorted.Length, medianMs = sorted[sorted.Length / 2], p95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], maxMs = sorted[^1] }; });
                return new { label, initialEquipmentCount = 6000, cards = 300, initialStacks = p.Inventory.Count, playerSaveBytes = new FileInfo(Path.Combine(save, "players.json")).Length, pageWrites, requests = report };
            }
        }
        object? before = baseline == null ? null : await Measure("baseline", baseline);
        object after = await Measure("optimized", null);
        var large = new GsGetUserCardListOk { totalDataCount = 64, dataCount = 64 };
        for (ulong i = 0; i < 64; i++) { var card = new PKUserCardInfo { cardID = 20103, cardSeq = i + 1 }; card.jewelID.AddRange(Enumerable.Repeat(uint.MaxValue, 100)); large.userCardInfoList.Add(card); }
        var parts = LocalRuntime.SerializeReplyParts(large).ToArray();
        Check(parts.Length > 1 && parts.All(x => x.Length + WireCodec.HeaderSize <= WireCodec.MaxPacketSize), "Oversize rows split by serialized byte limit");
        Check(parts.Sum(x => ((GsGetUserCardListOk)WireCodec.Deserialize(typeof(GsGetUserCardListOk), x)!).dataCount) == 64, "Multipart totals terminate exactly");
        var result = new { passed = true, count = checks.Count, checks, baseline = before, optimized = after, productionSavesUsed = false };
        File.WriteAllText(Path.Combine(root, "evidence/request-performance-test.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        System.Console.WriteLine(JsonSerializer.Serialize(new { result.passed, result.count, baseline = before, optimized = after }));
    }
}
