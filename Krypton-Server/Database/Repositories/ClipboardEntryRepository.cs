using Krypton.Server.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ClipboardContentType = Krypton.Shared.Protocol.ClipboardContentType;

namespace Krypton.Server.Database.Repositories;

public class ClipboardEntryRepository : IClipboardEntryRepository
{
    private readonly KryptonDbContext _context;

    public ClipboardEntryRepository(KryptonDbContext context)
    {
        _context = context;
    }

    public async Task<ClipboardEntry?> GetByIdAsync(Guid id)
    {
        return await _context.ClipboardEntries.FindAsync(id);
    }

    public async Task<IEnumerable<ClipboardEntry>> GetByUserIdAsync(Guid userId, int limit = 100, int offset = 0)
    {
        return await _context.ClipboardEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<ClipboardEntry>> SearchAsync(Guid userId, string query, int limit = 50)
    {
        var searchQuery = query.ToLowerInvariant();

        return await _context.ClipboardEntries
            .Where(e => e.UserId == userId)
            .Where(e => e.ContentType == ClipboardContentType.Text)
            .Where(e => e.ContentPreview != null && EF.Functions.Like(e.ContentPreview.ToLower(), $"%{searchQuery}%"))
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<ClipboardEntry> CreateAsync(ClipboardEntry entry)
    {
        entry.Id = Guid.NewGuid();
        entry.SyncedAt = DateTime.UtcNow;

        // Check for duplicate by hash
        var existing = await GetByHashAsync(entry.UserId, entry.ContentHash);
        if (existing != null)
        {
            // Move existing to top by updating its timestamp
            existing.CreatedAt = entry.CreatedAt;
            existing.SyncedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return existing;
        }

        _context.ClipboardEntries.Add(entry);
        await _context.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(ClipboardEntry entry)
    {
        _context.ClipboardEntries.Update(entry);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entry = await GetByIdAsync(id);
        if (entry != null)
        {
            _context.ClipboardEntries.Remove(entry);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<ClipboardEntry?> GetByHashAsync(Guid userId, string contentHash)
    {
        return await _context.ClipboardEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ContentHash == contentHash);
    }

    public async Task UpdateTimestampAsync(Guid entryId)
    {
        var entry = await GetByIdAsync(entryId);
        if (entry != null)
        {
            entry.CreatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> CleanupOldEntriesAsync(int days)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        var entriesToDelete = await _context.ClipboardEntries
            .Where(e => e.CreatedAt < cutoffDate)
            .ToListAsync();

        _context.ClipboardEntries.RemoveRange(entriesToDelete);
        await _context.SaveChangesAsync();

        return entriesToDelete.Count;
    }

    public async Task<int> GetCountByUserIdAsync(Guid userId)
    {
        return await _context.ClipboardEntries
            .CountAsync(e => e.UserId == userId);
    }
}
