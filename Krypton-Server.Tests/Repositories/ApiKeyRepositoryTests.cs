using FluentAssertions;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Krypton_Server.Tests.Repositories;

public class ApiKeyRepositoryTests : IDisposable
{
    private readonly KryptonDbContext _context;
    private readonly ApiKeyRepository _repository;
    private readonly Guid _testUserId;

    public ApiKeyRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<KryptonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new KryptonDbContext(options);
        _repository = new ApiKeyRepository(_context);
        _testUserId = Guid.NewGuid();

        // Create test user
        _context.Users.Add(new User
        {
            Id = _testUserId,
            Username = "testuser",
            PasswordHash = "hash",
            IsAdmin = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ShouldGenerateApiKey()
    {
        // Act
        var result = await _repository.CreateAsync(_testUserId, "Test Key");

        // Assert
        result.Should().NotBeNull();
        result.Key.Should().StartWith("kr_");
        result.Key.Should().HaveLength(35); // "kr_" + 32 chars
    }

    [Fact]
    public async Task GetByKeyAsync_ShouldReturnApiKey()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "Test Key");

        // Act
        var result = await _repository.GetByKeyAsync(apiKey.Key);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(apiKey.Id);
    }

    [Fact]
    public async Task GetByKeyAsync_NonExistent_ShouldReturnNull()
    {
        // Act
        var result = await _repository.GetByKeyAsync("kr_nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldReturnUserKeys()
    {
        // Arrange
        await _repository.CreateAsync(_testUserId, "Key 1");
        await _repository.CreateAsync(_testUserId, "Key 2");

        // Act
        var result = await _repository.GetByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task RevokeAsync_ShouldMarkKeyAsRevoked()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "To Revoke");

        // Act
        await _repository.RevokeAsync(apiKey.Id);

        // Assert
        var result = await _repository.GetByIdAsync(apiKey.Id);
        result!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_ShouldReturnTrue()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "Valid Key");

        // Act
        var result = await _repository.ValidateAsync(apiKey.Key);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_ShouldReturnFalse()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "Revoked Key");
        await _repository.RevokeAsync(apiKey.Id);

        // Act
        var result = await _repository.ValidateAsync(apiKey.Key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_ShouldReturnFalse()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "Expired Key");
        apiKey.ExpiresAt = DateTime.UtcNow.AddDays(-1); // Already expired
        await _repository.UpdateAsync(apiKey);

        // Act
        var result = await _repository.ValidateAsync(apiKey.Key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_InactiveUser_ShouldReturnFalse()
    {
        // Arrange
        var inactiveUserId = Guid.NewGuid();
        _context.Users.Add(new User
        {
            Id = inactiveUserId,
            Username = "inactive",
            PasswordHash = "hash",
            IsAdmin = false,
            IsActive = false, // Inactive user
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var apiKey = await _repository.CreateAsync(inactiveUserId, "Key for inactive user");

        // Act
        var result = await _repository.ValidateAsync(apiKey.Key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateLastUsedAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var apiKey = await _repository.CreateAsync(_testUserId, "Test Key");
        var originalLastUsed = apiKey.LastUsedAt;

        // Act
        await Task.Delay(10); // Ensure different timestamp
        await _repository.UpdateLastUsedAsync(apiKey.Key);

        // Assert
        var result = await _repository.GetByKeyAsync(apiKey.Key);
        result!.LastUsedAt.Should().NotBe(originalLastUsed);
        result.LastUsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllKeys()
    {
        // Arrange
        await _repository.CreateAsync(_testUserId, "Key 1");
        await _repository.CreateAsync(_testUserId, "Key 2");

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }
}
