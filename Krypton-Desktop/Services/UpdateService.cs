using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Krypton.Shared.Protocol;

namespace Krypton_Desktop.Services;

public class UpdateService
{
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Krypton-Desktop" } }
    };

    /// <summary>
    /// Returns the latest release version and asset download URL, or null on failure / no update.
    /// </summary>
    public async Task<(string Version, string DownloadUrl)?> GetLatestReleaseAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(PacketConstants.GitHubReleasesApi);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            // tag format: "1.0.0-abc1234" → version = "1.0.0"
            var version = tagName.Split('-')[0];

            var assetName = GetPlatformAssetName();
            if (string.IsNullOrEmpty(assetName))
                return null;

            var assets = root.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == assetName)
                {
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    return (version, url);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/>,
    /// reporting progress in the range [0, 1].
    /// </summary>
    public async Task DownloadAsync(string url, string destPath, IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 8192, useAsync: true);

        var buffer = new byte[8192];
        long downloadedBytes = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
                progress?.Report((double)downloadedBytes / totalBytes);
        }
    }

    /// <summary>
    /// Replaces the running binary with the downloaded update and restarts.
    /// On macOS, opens the GitHub releases page instead.
    /// </summary>
    public async Task ApplyUpdateAsync(string tempFilePath)
    {
        if (OperatingSystem.IsMacOS())
        {
            OpenReleasesPage();
            return;
        }

        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.GetCommandLineArgs()[0];

        if (OperatingSystem.IsWindows())
        {
            var installType = ReadRegistryString("InstallType");
            if (installType == "setup")
            {
                var variant = ReadRegistryString("Variant") ?? "selfcontained";
                var task = variant == "framework" ? "fd" : "sc";
                Process.Start(new ProcessStartInfo(tempFilePath,
                    $"/VERYSILENT /NORESTART /Tasks=\"{task},startup\"")
                    { UseShellExecute = true });
                Environment.Exit(0);
                return;
            }

            // Portable path: use PowerShell to copy-over running EXE then restart
            var scriptPath = Path.Combine(Path.GetTempPath(), "krypton-update.ps1");
            var script = $"""
                Start-Sleep -Seconds 2
                Copy-Item '{tempFilePath}' '{currentExe}' -Force
                Start-Process '{currentExe}'
                """;
            await File.WriteAllTextAsync(scriptPath, script);
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
                { UseShellExecute = true });
            Environment.Exit(0);
        }
        else if (OperatingSystem.IsLinux())
        {
            File.Copy(tempFilePath, currentExe, overwrite: true);
            Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true });
            Environment.Exit(0);
        }
    }

    public void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo(
            $"https://github.com/{PacketConstants.GitHubOwner}/{PacketConstants.GitHubRepo}/releases/latest")
            { UseShellExecute = true });
    }

    public static string GetPlatformAssetName()
    {
        if (OperatingSystem.IsWindows())
        {
            var installType = ReadRegistryString("InstallType");
            return installType == "setup"
                ? "krypton-desktop-win-x64-setup.exe"
                : "krypton-desktop-win-x64-portable.exe";
        }
        if (OperatingSystem.IsLinux()) return "krypton-desktop-linux-x64";
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "krypton-desktop-osx-arm64.zip"
                : "krypton-desktop-osx-x64.zip";
        }
        return string.Empty;
    }

#pragma warning disable CA1416
    private static string? ReadRegistryString(string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Krypton");
        return key?.GetValue(valueName) as string;
    }
#pragma warning restore CA1416
}
