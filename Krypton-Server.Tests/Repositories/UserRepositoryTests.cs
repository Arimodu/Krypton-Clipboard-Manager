using FluentAssertions;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Krypton_Server.Tests.Repositories;

public class UserRepositoryTests : IDisposable
{
    private readonly KryptonDbContext _context;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<KryptonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new KryptonDbContext(options);
        _repository = new UserRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private User CreateTestUser(string username = "testuser")
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            IsAdmin = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task CreateAsync_ShouldAddUser()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var result = await _repository.CreateAsync(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnUser()
    {
        // Arrange
        var user = CreateTestUser();
        await _repository.CreateAsync(user);

        // Act
        var result = await _repository.GetByIdAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ShouldReturnNull()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByUsernameAsync_ShouldReturnUser()
    {
        // Arrange
        var user = CreateTestUser("uniqueuser");
        await _repository.CreateAsync(user);

        // Act
        var result = await _repository.GetByUsernameAsync("uniqueuser");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByUsernameAsync_IsCaseInsensitive()
    {
        // Arrange
        var user = CreateTestUser("TestUser");
        await _repository.CreateAsync(user);

        // Act - different case
        var result = await _repository.GetByUsernameAsync("testuser");

        // Assert - EF Core InMemory is case-insensitive
        result.Should().NotBeNull();
        result!.Username.Should().Be("TestUser");
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllUsers()
    {
        // Arrange
        await _repository.CreateAsync(CreateTestUser("user1"));
        await _repository.CreateAsync(CreateTestUser("user2"));
        await _repository.CreateAsync(CreateTestUser("user3"));

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyUser()
    {
        // Arrange
        var user = CreateTestUser();
        await _repository.CreateAsync(user);
        user.IsAdmin = true;

        // Act
        await _repository.UpdateAsync(user);

        // Assert
        var result = await _repository.GetByIdAsync(user.Id);
        result!.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveUser()
    {
        // Arrange
        var user = CreateTestUser();
        await _repository.CreateAsync(user);

        // Act
        await _repository.DeleteAsync(user.Id);

        // Assert
        var result = await _repository.GetByIdAsync(user.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ExistingUser_ShouldReturnTrue()
    {
        // Arrange
        await _repository.CreateAsync(CreateTestUser("existing"));

        // Act
        var result = await _repository.ExistsAsync("existing");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistent_ShouldReturnFalse()
    {
        // Act
        var result = await _repository.ExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateLastLoginAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var user = CreateTestUser();
        user.LastLoginAt = null;
        await _repository.CreateAsync(user);

        // Act
        await _repository.UpdateLastLoginAsync(user.Id);

        // Assert
        var result = await _repository.GetByIdAsync(user.Id);
        result!.LastLoginAt.Should().NotBeNull();
        result.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
