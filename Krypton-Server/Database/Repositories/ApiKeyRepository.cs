using System.Security.Cryptography;
using Krypton.Server.Database.Entities;
using Krypton.Shared.Protocol;
using Microsoft.EntityFrameworkCore;

namespace Krypton.Server.Database.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly KryptonDbContext _context;

    public ApiKeyRepository(KryptonDbContext context)
    {
        _context = context;
    }

    public async Task<ApiKey?> GetByIdAsync(Guid id)
    {
        return await _context.ApiKeys
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<ApiKey?> GetByKeyAsync(string key)
    {
        return await _context.ApiKeys
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Key == key);
    }

    public async Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId)
    {
        return await _context.ApiKeys
            .Where(a => a.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ApiKey>> GetAllAsync()
    {
        return await _context.ApiKeys
            .Include(a => a.User)
            .ToListAsync();
    }

    public async Task<ApiKey> CreateAsync(Guid userId, string name)
    {
        var apiKey = new ApiKey
        {
            UserId = userId,
            Name = name,
            Id = Guid.NewGuid(),
            Key = GenerateApiKey(),
            CreatedAt = DateTime.UtcNow
        };
        _context.ApiKeys.Add(apiKey);
        await _context.SaveChangesAsync();
        return apiKey;
    }

    public async Task UpdateAsync(ApiKey apiKey)
    {
        _context.ApiKeys.Update(apiKey);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var apiKey = await GetByIdAsync(id);
        if (apiKey != null)
        {
            _context.ApiKeys.Remove(apiKey);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeAsync(Guid id)
    {
        var apiKey = await GetByIdAsync(id);
        if (apiKey != null)
        {
            apiKey.IsRevoked = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateLastUsedAsync(string key)
    {
        var apiKey = await GetByKeyAsync(key);
        if (apiKey != null)
        {
            apiKey.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ValidateAsync(string key)
    {
        var apiKey = await GetByKeyAsync(key);
        if (apiKey == null) return false;
        if (apiKey.IsRevoked) return false;
        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow) return false;
        if (!apiKey.User.IsActive) return false;
        return true;
    }

    public static string GenerateApiKey()
    {
        var bytes = new byte[PacketConstants.ApiKeyLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"{PacketConstants.ApiKeyPrefix}{base64[..PacketConstants.ApiKeyLength]}";
    }
}
