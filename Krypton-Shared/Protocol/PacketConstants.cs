using System.Linq;
using System.Reflection;

namespace Krypton.Shared.Protocol;

public static class PacketConstants
{
    public const int DefaultPort = 6789;

    // GitHub release endpoint (used by both server upgrade and desktop updater)
    public const string GitHubOwner = "Arimodu";
    public const string GitHubRepo  = "Krypton-Clipboard-Manager";
    public const string GitHubReleasesApi =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    /// <summary>Base semantic version (X.Y.Z) from the entry assembly.</summary>
    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Commit SHA baked in by CI via -p:SourceRevisionId={sha}.</summary>
    public static string CommitHash =>
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+').ElementAtOrDefault(1) ?? string.Empty;

    /// <summary>"1.0.0+abc1234" for CI builds, "1.0.0" for local builds.</summary>
    public static string FullVersion =>
        CommitHash.Length > 0 ? $"{AppVersion}+{CommitHash}" : AppVersion;

    // Backward-compat aliases
    public static string ClientVersion => AppVersion;
    public static string ServerVersion => AppVersion;

    public const int HeaderSize = 6; // 4 bytes length + 2 bytes type
    public const int MaxPacketSize = 10 * 1024 * 1024; // 10 MB

    public const int HeartbeatIntervalMs = 30000;
    public const int ConnectionTimeoutMs = 120000;

    public const string ApiKeyPrefix = "kr_";
    public const int ApiKeyLength = 32;
}
