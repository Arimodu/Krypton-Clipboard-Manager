using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;

namespace Krypton.Server.Services;

/// <summary>
/// Handles user authentication via password or API key.
/// </summary>
public class AuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IApiKeyRepository _apiKeyRepository;

    public AuthenticationService(
        IUserRepository userRepository,
        IApiKeyRepository apiKeyRepository)
    {
        _userRepository = userRepository;
        _apiKeyRepository = apiKeyRepository;
    }

    public async Task<AuthResult> AuthenticateWithPasswordAsync(string username, string password)
    {
        var user = await _userRepository.GetByUsernameAsync(username);

        if (user == null)
        {
            return AuthResult.Failed("Invalid username or password");
        }

        if (!user.IsActive)
        {
            return AuthResult.Failed("Account is disabled");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return AuthResult.Failed("Invalid username or password");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return AuthResult.Success(user);
    }

    public async Task<AuthResult> AuthenticateWithApiKeyAsync(string apiKey)
    {
        var key = await _apiKeyRepository.GetByKeyAsync(apiKey);

        if (key == null)
        {
            return AuthResult.Failed("Invalid API key");
        }

        if (key.IsRevoked)
        {
            return AuthResult.Failed("API key has been revoked");
        }

        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
        {
            return AuthResult.Failed("API key has expired");
        }

        var user = key.User ?? await _userRepository.GetByIdAsync(key.UserId);
        if (user == null || !user.IsActive)
        {
            return AuthResult.Failed("Account is disabled");
        }

        key.LastUsedAt = DateTime.UtcNow;
        await _apiKeyRepository.UpdateAsync(key);

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return AuthResult.Success(user);
    }

    public async Task<(User User, string ApiKey)?> RegisterUserAsync(
        string username,
        string password,
        string? deviceName = null)
    {
        if (await _userRepository.ExistsAsync(username))
        {
            return null;
        }

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = false,
            IsActive = true
        };

        await _userRepository.CreateAsync(user);

        var createdKey = await _apiKeyRepository.CreateAsync(user.Id, deviceName ?? "Registration");

        return (user, createdKey.Key);
    }
}

public class AuthResult
{
    public bool IsSuccess { get; private init; }
    public User? User { get; private init; }
    public string? Error { get; private init; }

    public static AuthResult Success(User user) => new()
    {
        IsSuccess = true,
        User = user
    };

    public static AuthResult Failed(string error) => new()
    {
        IsSuccess = false,
        Error = error
    };
}
