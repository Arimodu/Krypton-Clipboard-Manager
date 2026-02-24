using System.Text.Json;
using Krypton.Shared.Protocol;

namespace Krypton.Server.Services;

public class UpdateService
{
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Krypton-Server" } }
    };

    /// <summary>
    /// Returns the latest release version and asset download URL, or null on failure.
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

            const string assetName = "krypton-server-linux-x64";
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
    /// Downloads the file at <paramref name="url"/> to <paramref name="destPath"/>,
    /// reporting progress in the range [0, 1].
    /// </summary>
    public async Task DownloadAsync(string url, string destPath, IProgress<double>? progress = null)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 8192, useAsync: true);

        var buffer = new byte[8192];
        long downloadedBytes = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
                progress?.Report((double)downloadedBytes / totalBytes);
        }
    }
}
