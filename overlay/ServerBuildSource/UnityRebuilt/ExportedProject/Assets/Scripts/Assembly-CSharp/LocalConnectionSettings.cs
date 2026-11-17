using System;

// C# 3 / Unity 5.6 compatible. Compiled unchanged into the server core too.
namespace Bnsm.Server
{
    public sealed class GameDirectoryEndpoint
    {
        public string Host { get; private set; }
        public int Port { get; private set; }
        public GameDirectoryEndpoint(string host, int port) { Host = host; Port = port; }
    }

    /// <summary>Only local mode is enabled by current launchers. External is a future host opt-in.</summary>
    public sealed class GameConnectionSettings
    {
        public string Mode { get; set; }
        public string ExternalHost { get; set; }
        public int ExternalDirectoryPort { get; set; }
        public GameConnectionSettings() { Mode = "local"; ExternalHost = ""; ExternalDirectoryPort = 10101; }
        public GameDirectoryEndpoint Resolve() { return Resolve(10101, false); }
        public GameDirectoryEndpoint Resolve(int localDirectoryPort) { return Resolve(localDirectoryPort, false); }
        public GameDirectoryEndpoint Resolve(bool externalEnabled) { return Resolve(10101, externalEnabled); }
        public GameDirectoryEndpoint Resolve(int localDirectoryPort, bool externalEnabled)
        {
            if (Mode == "local") return new GameDirectoryEndpoint("127.0.0.1", ValidPort(localDirectoryPort));
            if (Mode != "external") throw new ArgumentException("Unknown connection mode");
            if (!externalEnabled) throw new NotSupportedException("External servers are reserved for a future release");
            if (string.IsNullOrEmpty(ExternalHost) || Uri.CheckHostName(ExternalHost) == UriHostNameType.Unknown)
                throw new ArgumentException("Invalid external host");
            return new GameDirectoryEndpoint(ExternalHost, ValidPort(ExternalDirectoryPort));
        }
        private static int ValidPort(int value)
        {
            if (value <= 0 || value > 65535) throw new ArgumentOutOfRangeException("value");
            return value;
        }
    }
}
