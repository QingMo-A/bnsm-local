using System.Net;

namespace Bnsm.Server;

/// <summary>Host boundary for a future Unity/Android bridge; no ASP.NET or Windows process.</summary>
public interface IEmbeddedGameServer : IAsyncDisposable
{
    int DirectoryPort { get; }
    int LoginPort { get; }
    int GamePort { get; }
    string SaveDirectory { get; }
    void Checkpoint();
}

/// <summary>Separate binding and advertised address so directory/login redirects can be deployed externally later.</summary>
public sealed record ServerEndpointOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public IPAddress AdvertisedAddress { get; init; } = IPAddress.Loopback;
    public int DirectoryPort { get; init; } = 10101;
    public int LoginPort { get; init; } = 10102;
    public int GamePort { get; init; } = 10103;

    internal void Validate()
    {
        if (ListenAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            AdvertisedAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            AdvertisedAddress.Equals(IPAddress.Any)) throw new ArgumentException("Original protocol requires an advertised IPv4 address");
        foreach (int port in new[] { DirectoryPort, LoginPort, GamePort })
            if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
    }
}

/// <summary>Same catalog, protocol and schema-1 saves as the desktop server. Caller owns app lifecycle.</summary>
public sealed class EmbeddedServer : IEmbeddedGameServer
{
    private readonly LocalRuntime runtime;
    private readonly LocalDebugService debug;
    public int DirectoryPort => runtime.DirectoryPort;
    public int LoginPort => runtime.LoginPort;
    public int GamePort => runtime.GamePort;
    public string SaveDirectory { get; }
    public int ActiveGameConnections=>runtime.ActiveGameConnections;
    public DateTime? LastGameRequestUtc=>runtime.LastGameRequestUtc;
    public const int SaveSchema = 1;
    public EmbeddedServer(string contentRoot, string saveDirectory, ServerEndpointOptions? endpoints = null)
    {
        SaveDirectory = Path.GetFullPath(saveDirectory);
        SaveWorkspace.ValidateData(SaveDirectory);
        Directory.CreateDirectory(SaveDirectory);
        string format = Path.Combine(SaveDirectory, "save-format.json");
        if (!File.Exists(format))
        {
            string temp = format + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(new SaveFormat()));
                for (int attempt = 0; ; attempt++)
                    try { File.Move(temp, format, true); break; }
                    catch (IOException ex) when (attempt < 8 && (ex.HResult & 0xffff) is 32 or 33)
                    { Thread.Sleep(25 * (attempt + 1)); }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        runtime = new LocalRuntime(Path.GetFullPath(contentRoot), SaveDirectory, endpoints: endpoints ?? new());
        debug = new LocalDebugService(runtime);
    }
    internal object HandleDebug(string method, string path, uint? uid, string? body) => debug.Handle(method, path, uid, body);
    public void Checkpoint() => runtime.Store.Checkpoint();
    public Dictionary<string, byte[]> CaptureSave()
    {
        Dictionary<string,byte[]> snapshot;
        lock (runtime.Store.Gate)
        {
            runtime.Store.Checkpoint();
            snapshot = new[] { "players.json", "save-format.json" }
                .Where(name => File.Exists(Path.Combine(SaveDirectory, name)))
                .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(SaveDirectory, name)));
        }
        // These domains have independent atomic commits. Take each file under
        // its existing writer gate, without nesting player/chat/bot locks.
        snapshot["bot-world.json"]=runtime.Router.Bots.CaptureSave();
        snapshot["world-chat.json"]=runtime.Router.CaptureAmbientSave();
        return snapshot;
    }
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}
