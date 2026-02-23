using Krypton.Server.Database.Entities;

namespace Krypton.Server.Database.Repositories;

public interface IClipboardEntryRepository
{
    Task<ClipboardEntry?> GetByIdAsync(Guid id);
    Task<IEnumerable<ClipboardEntry>> GetByUserIdAsync(Guid userId, int limit = 100, int offset = 0);
    Task<IEnumerable<ClipboardEntry>> SearchAsync(Guid userId, string query, int limit = 50);
    Task<ClipboardEntry> CreateAsync(ClipboardEntry entry);
    Task UpdateAsync(ClipboardEntry entry);
    Task DeleteAsync(Guid id);
    Task<ClipboardEntry?> GetByHashAsync(Guid userId, string contentHash);
    Task UpdateTimestampAsync(Guid entryId);
    Task<int> CleanupOldEntriesAsync(int days);
    Task<int> GetCountByUserIdAsync(Guid userId);
}
