using System.Reflection;

namespace Krypton.Shared.Protocol;

public static class PacketConstants
{
    public const int DefaultPort = 6789;

    /// <summary>
    /// Gets the client version from the entry assembly or falls back to "1.0.0".
    /// </summary>
    public static string ClientVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Gets the server version from the entry assembly or falls back to "1.0.0".
    /// </summary>
    public static string ServerVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public const int HeaderSize = 6; // 4 bytes length + 2 bytes type
    public const int MaxPacketSize = 10 * 1024 * 1024; // 10 MB

    public const int HeartbeatIntervalMs = 30000;
    public const int ConnectionTimeoutMs = 120000;

    public const string ApiKeyPrefix = "kr_";
    public const int ApiKeyLength = 32;
}
