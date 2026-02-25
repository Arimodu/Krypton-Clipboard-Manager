using Krypton.Server.Configuration;
using Krypton.Server.Database.Repositories;
using Krypton.Server.Networking;
using Krypton.Shared.Protocol;
using DbClipboardEntry = Krypton.Server.Database.Entities.ClipboardEntry;

namespace Krypton.Server.Services;

/// <summary>
/// Handles clipboard entry storage and synchronization.
/// </summary>
public class ClipboardService
{
    private readonly IClipboardEntryRepository _repository;
    private readonly ConnectionManager _connectionManager;
    private readonly ServerConfiguration _config;

    public ClipboardService(
        IClipboardEntryRepository repository,
        ConnectionManager connectionManager,
        ServerConfiguration config)
    {
        _repository = repository;
        _connectionManager = connectionManager;
        _config = config;
    }

    public async Task<DbClipboardEntry> PushEntryAsync(
        Guid userId,
        ClipboardContentType contentType,
        byte[] content,
        string? preview,
        string? sourceDevice,
        Guid senderConnectionId,
        CancellationToken cancellationToken)
    {
        var entry = new DbClipboardEntry
        {
            UserId = userId,
            ContentType = contentType,
            Content = content,
            ContentPreview = preview ?? GeneratePreview(content, contentType),
            ContentHash = ComputeHash(content),
            SourceDevice = sourceDevice
        };

        // Store image externally if configured
        if (contentType == ClipboardContentType.Image
            && _config.Images.StorageMode == ImageStorageMode.FileSystem
            && content.Length > 0)
        {
            var relativePath = Path.Combine("images", userId.ToString(), $"{Guid.NewGuid()}.png");
            var fullPath = Path.Combine(_config.Images.StoragePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
            entry.ExternalStoragePath = relativePath;
            entry.Content = [];
        }

        await _repository.CreateAsync(entry);

        // Broadcast to all other connections of this user
        var broadcast = new ClipboardBroadcast
        {
            Entry = ToProtoEntry(entry)
        };

        await _connectionManager.BroadcastAsync(
            PacketType.ClipboardBroadcast,
            broadcast,
            excludeConnectionId: senderConnectionId,
            onlyUserId: userId,
            cancellationToken: cancellationToken);

        return entry;
    }

    public async Task<IEnumerable<DbClipboardEntry>> GetHistoryAsync(
        Guid userId,
        int limit = 100,
        int offset = 0)
    {
        return await _repository.GetByUserIdAsync(userId, limit, offset);
    }

    public async Task<int> GetHistoryCountAsync(Guid userId)
    {
        return await _repository.GetCountByUserIdAsync(userId);
    }

    public async Task<IEnumerable<DbClipboardEntry>> SearchAsync(
        Guid userId,
        string query,
        int limit = 50)
    {
        return await _repository.SearchAsync(userId, query, limit);
    }

    public async Task<bool> MoveToTopAsync(Guid userId, Guid entryId)
    {
        var entry = await _repository.GetByIdAsync(entryId);
        if (entry == null || entry.UserId != userId)
        {
            return false;
        }

        await _repository.UpdateTimestampAsync(entryId);
        return true;
    }

    public async Task<bool> DeleteEntryAsync(Guid userId, Guid entryId)
    {
        var entry = await _repository.GetByIdAsync(entryId);
        if (entry == null || entry.UserId != userId)
        {
            return false;
        }

        await _repository.DeleteAsync(entryId);
        return true;
    }

    public Krypton.Shared.Protocol.ClipboardEntry ToProtoEntry(DbClipboardEntry entry)
    {
        var content = entry.Content;

        // Hydrate externally stored image content before sending to client
        if (entry.ExternalStoragePath != null)
        {
            var fullPath = Path.Combine(_config.Images.StoragePath, entry.ExternalStoragePath);
            if (File.Exists(fullPath))
            {
                content = File.ReadAllBytes(fullPath);
            }
        }

        return new Krypton.Shared.Protocol.ClipboardEntry
        {
            Id = entry.Id.ToString(),
            ContentType = entry.ContentType,
            Content = Google.Protobuf.ByteString.CopyFrom(content),
            ContentPreview = entry.ContentPreview ?? "",
            SourceDevice = entry.SourceDevice ?? "",
            ContentHash = entry.ContentHash,
            CreatedAt = (ulong)new DateTimeOffset(entry.CreatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds()
        };
    }

    private static string GeneratePreview(byte[] content, ClipboardContentType contentType)
    {
        if (contentType == ClipboardContentType.Text)
        {
            var text = System.Text.Encoding.UTF8.GetString(content);
            return text.Length > 200 ? text[..200] + "..." : text;
        }

        return contentType switch
        {
            ClipboardContentType.Image => "[Image]",
            ClipboardContentType.File => "[File]",
            _ => "[Unknown]"
        };
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(content);
        return Convert.ToHexString(hash);
    }
}
