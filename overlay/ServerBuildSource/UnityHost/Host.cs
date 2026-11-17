using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Bnsm.Server;

namespace Bnsm.UnityHost;

/// <summary>Unity owns this instance for its entire process. No Unity API is used on worker threads.</summary>
public static class Host
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static EmbeddedServer? server;
    private static EmbeddedHttpHost? http;
    private static ManagedSaveSession? session;
    private static ManagedSaveWorkspace? workspace;
    private static ServerDiagnostics? diagnostics;
    private static Timer? timer;
    private static long ticks;
    private static string instance = "", state = "等待选择存档", failure = "", content = "", root = "", evidence = "";
    private static volatile bool ready;
    private static string lastCheckpointUtc="";
    private static double lastCheckpointMilliseconds;
    private static object? lastSupportExport;
    public static bool Ready => ready;
    public static bool HasWorkspace(string directory) => File.Exists(Path.Combine(directory, "workspace.json"));
    public static string Status => state;
    public static object Snapshot() => new { ready = Ready, runRequested = Ready, instanceId = instance,
        state, error = failure, processId = System.Diagnostics.Process.GetCurrentProcess().Id,
        embedded = true, schema = EmbeddedServer.SaveSchema, heartbeat = Interlocked.Read(ref ticks),
        activeGameConnections = server?.ActiveGameConnections ?? 0, profile = session?.Name,
        lastCheckpointUtc,lastCheckpointMilliseconds,lastSupportExport,
        lastGameRequestUtc = server?.LastGameRequestUtc, lastStopReason = Ready ? "" : state };

    public static async Task Start(byte[] archive, string workspaceRoot, string evidencePath)
    {
        await gate.WaitAsync();
        try
        {
            if (server != null) return;
            root = Path.GetFullPath(workspaceRoot); evidence = evidencePath;
            diagnostics ??= ServerDiagnostics.Begin(Path.Combine(root, "Diagnostics"));
            diagnostics.ContextProvider = Snapshot;
            state = "校验内置服务端数据"; failure = "";
            content = Extract(archive, Path.Combine(root, "Content"));
            workspace = new ManagedSaveWorkspace(Path.Combine(root, "Saves"));
            session = workspace.Open(recoverAndroidBotNumbers:true);
            state = "启动内置服务端";
            server = new EmbeddedServer(content, session.Directory, new ServerEndpointOptions
                { DirectoryPort = 10111, LoginPort = 10112, GamePort = 10113 });
            try { http = new EmbeddedHttpHost(server); }
            catch { await server.DisposeAsync(); server = null; throw; }
            instance = Guid.NewGuid().ToString("N"); state = "内置服务端已就绪"; ready = true;
            timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            ServerDiagnostics.Info("Embedded host ready; same process; isolated model; original protocol; profile=" + session.Id);
        }
        catch (Exception error)
        {
            ready=false;
            if(http!=null){try{await http.DisposeAsync();}catch(Exception cleanup){ServerDiagnostics.Error(cleanup.ToString());}http=null;}
            if(server!=null){try{await server.DisposeAsync();}catch(Exception cleanup){ServerDiagnostics.Error(cleanup.ToString());}server=null;}
            session?.Dispose(); session = null;
            failure = error.Message; state = "内置服务端启动失败：" + failure;
            ServerDiagnostics.Error(error.ToString()); Tick(); throw;
        }
        finally { gate.Release(); }
    }
    private static void Tick()
    {
        Interlocked.Increment(ref ticks);
        try
        {
            // Diagnostic evidence has no player inventory, passwords or save contents.
            string json = JsonSerializer.Serialize(Snapshot());
            string temp = evidence + ".tmp"; File.WriteAllText(temp, json);
            Bnsm.UnityCompat.Api.Move(temp, evidence, true);
        }
        catch (Exception error) { ServerDiagnostics.Error("Embedded heartbeat: " + error.Message); }
    }
    private static string Extract(byte[] bytes, string parent)
    {
        string hash = Bnsm.UnityCompat.Api.ToHexString(Bnsm.UnityCompat.Api.Sha256(bytes));
        string target = Path.GetFullPath(Path.Combine(parent, hash)); Directory.CreateDirectory(target);
        // Verify every file on each launch; a marker alone cannot validate interrupted/corrupt extraction.
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!path.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                entry.Length > 32 * 1024 * 1024 || (total += entry.Length) > 128 * 1024 * 1024)
                throw new InvalidDataException("内置数据路径或大小无效");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            using var input = entry.Open(); using var buffer = new MemoryStream(); input.CopyTo(buffer);
            byte[] source = buffer.ToArray();
            if (File.Exists(path) && new FileInfo(path).Length == source.Length &&
                Bnsm.UnityCompat.Api.ToHexString(Bnsm.UnityCompat.Api.Sha256(File.ReadAllBytes(path))) ==
                Bnsm.UnityCompat.Api.ToHexString(Bnsm.UnityCompat.Api.Sha256(source))) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path + ".tmp", source); Bnsm.UnityCompat.Api.Move(path + ".tmp", path, true);
        }
        return target;
    }
    private static async Task StopLocked()
    {
        ready = false;
        timer?.Dispose(); timer = null;
        if (http != null) { await http.DisposeAsync(); http = null; }
        if (server != null) { await server.DisposeAsync(); server = null; }
        session?.Dispose(); session = null; state = "内置服务端已保存并停止";
    }
    public static async Task Stop()
    { await gate.WaitAsync(); try { await StopLocked(); } finally { gate.Release(); } }
    public static async Task Checkpoint()
    {
        await gate.WaitAsync();
        try
        {
            if(server==null) return;
            var watch=System.Diagnostics.Stopwatch.StartNew();server.Checkpoint();
            lastCheckpointUtc=DateTime.UtcNow.ToString("O");lastCheckpointMilliseconds=watch.Elapsed.TotalMilliseconds;
        }
        finally { gate.Release(); }
    }
    public static async Task Import(string workspaceRoot, string path)
    {
        await gate.WaitAsync();
        try
        {
            await StopLocked();
            var manager = new ManagedSaveWorkspace(Path.Combine(workspaceRoot, "Saves"));
            // Import validates checksums/schema into a new profile; no existing profile is overwritten.
            var result = manager.Execute("import", name: "导入的手机进度", file: path);
            string id = result.GetProperty("profile").GetProperty("Id").GetString()!;
            manager.Execute("switch", id: id);
        }
        finally { gate.Release(); }
    }
    public static async Task Export(string path)
    {
        await gate.WaitAsync();
        try
        {
            if (workspace == null || session == null || server == null) throw new InvalidOperationException("内置服尚未准备完成，请稍后重试");
            ExportLocked(path);
        }
        finally { gate.Release(); }
    }
    // Decide inside the host gate, after any startup/shutdown ahead of this
    // request. A Unity-frame readiness cache must never omit a live save.
    // Only an unready host returns a reason; snapshot/write errors propagate.
    public static async Task<string> ExportForSupport(string path)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        await gate.WaitAsync();
        try
        {
            if (!ready || workspace == null || session == null || server == null)
                return "导出时内置服务端尚未就绪：" + state;
            ExportLocked(path,watch.Elapsed.TotalMilliseconds);
            return "";
        }
        finally { gate.Release(); }
    }
    private static void ExportLocked(string path,double gateWaitMilliseconds=0)
    {
        // Capture committed bytes under domain writer gates; release them
        // before ZIP compression and retain the native session/listeners.
        var watch=System.Diagnostics.Stopwatch.StartNew();var snapshot=server!.CaptureSave();
        double captureMilliseconds=watch.Elapsed.TotalMilliseconds;watch.Restart();
        if (File.Exists(path)) File.Delete(path);
        // Interactive phone exports favour CPU time over a small size saving.
        // The original archive schema/checksums and ordinary desktop policy stay unchanged.
        workspace!.ExportSnapshot(session!,path,snapshot,CompressionLevel.Fastest);
        lastSupportExport=new {completedUtc=DateTime.UtcNow,gateWaitMilliseconds,captureMilliseconds,
            archiveMilliseconds=watch.Elapsed.TotalMilliseconds,uncompressedBytes=snapshot.Sum(x=>(long)x.Value.Length),
            archiveBytes=new FileInfo(path).Length,files=snapshot.Count,compression="Fastest"};
        ServerDiagnostics.Info("Support export timings: "+JsonSerializer.Serialize(lastSupportExport));
    }
}
