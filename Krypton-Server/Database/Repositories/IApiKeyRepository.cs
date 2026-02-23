using Krypton.Server.Database.Entities;

namespace Krypton.Server.Database.Repositories;

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id);
    Task<ApiKey?> GetByKeyAsync(string key);
    Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<ApiKey>> GetAllAsync();
    Task<ApiKey> CreateAsync(Guid userId, string name);
    Task UpdateAsync(ApiKey apiKey);
    Task DeleteAsync(Guid id);
    Task RevokeAsync(Guid id);
    Task UpdateLastUsedAsync(string key);
    Task<bool> ValidateAsync(string key);
}
