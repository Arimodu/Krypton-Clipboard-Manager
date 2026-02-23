using FluentAssertions;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using ClipboardContentType = Krypton.Shared.Protocol.ClipboardContentType;

namespace Krypton_Server.Tests.Repositories;

public class ClipboardEntryRepositoryTests : IDisposable
{
    private readonly KryptonDbContext _context;
    private readonly ClipboardEntryRepository _repository;
    private readonly Guid _testUserId;

    public ClipboardEntryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<KryptonDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new KryptonDbContext(options);
        _repository = new ClipboardEntryRepository(_context);
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

    private ClipboardEntry CreateTestEntry(string content, string? hash = null)
    {
        return new ClipboardEntry
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            ContentType = ClipboardContentType.Text,
            Content = System.Text.Encoding.UTF8.GetBytes(content),
            ContentPreview = content.Length > 200 ? content[..200] : content,
            ContentHash = hash ?? Guid.NewGuid().ToString("N"),
            SourceDevice = "TestDevice",
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task CreateAsync_ShouldAddEntry()
    {
        // Arrange
        var entry = CreateTestEntry("Hello World");

        // Act
        var result = await _repository.CreateAsync(entry);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);

        var retrieved = await _repository.GetByIdAsync(result.Id);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DuplicateHash_ShouldUpdateExisting()
    {
        // Arrange
        var hash = "duplicate-hash";
        var entry1 = CreateTestEntry("First", hash);
        var entry2 = CreateTestEntry("Second", hash);

        // Act
        var result1 = await _repository.CreateAsync(entry1);
        await Task.Delay(10); // Ensure different timestamps
        var result2 = await _repository.CreateAsync(entry2);

        // Assert
        result1.Id.Should().Be(result2.Id); // Same entry returned
        var entries = await _repository.GetByUserIdAsync(_testUserId);
        entries.Should().HaveCount(1); // Only one entry exists
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldReturnEntriesInOrder()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
        {
            var entry = CreateTestEntry($"Entry {i}");
            entry.CreatedAt = DateTime.UtcNow.AddMinutes(-i); // Older entries
            await _repository.CreateAsync(entry);
        }

        // Act
        var entries = await _repository.GetByUserIdAsync(_testUserId);

        // Assert
        entries.Should().HaveCount(5);
        // Should be ordered by CreatedAt DESC (newest first)
        entries.First().ContentPreview.Should().Be("Entry 1");
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldRespectLimit()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _repository.CreateAsync(CreateTestEntry($"Entry {i}"));
        }

        // Act
        var entries = await _repository.GetByUserIdAsync(_testUserId, limit: 5);

        // Assert
        entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldRespectOffset()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            var entry = CreateTestEntry($"Entry {i}");
            entry.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            await _repository.CreateAsync(entry);
        }

        // Act
        var entries = await _repository.GetByUserIdAsync(_testUserId, limit: 5, offset: 5);

        // Assert
        entries.Should().HaveCount(5);
        entries.First().ContentPreview.Should().Be("Entry 6");
    }

    [Fact]
    public async Task SearchAsync_ShouldFindMatchingEntries()
    {
        // Arrange
        await _repository.CreateAsync(CreateTestEntry("Hello World"));
        await _repository.CreateAsync(CreateTestEntry("Goodbye World"));
        await _repository.CreateAsync(CreateTestEntry("Hello Universe"));

        // Act
        var results = await _repository.SearchAsync(_testUserId, "Hello");

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        await _repository.CreateAsync(CreateTestEntry("HELLO World"));

        // Act
        var results = await _repository.SearchAsync(_testUserId, "hello");

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntry()
    {
        // Arrange
        var entry = await _repository.CreateAsync(CreateTestEntry("To Delete"));

        // Act
        await _repository.DeleteAsync(entry.Id);

        // Assert
        var result = await _repository.GetByIdAsync(entry.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTimestampAsync_ShouldMoveToTop()
    {
        // Arrange
        var entry1 = CreateTestEntry("First");
        entry1.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        await _repository.CreateAsync(entry1);

        var entry2 = CreateTestEntry("Second");
        entry2.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        await _repository.CreateAsync(entry2);

        // Act
        await _repository.UpdateTimestampAsync(entry1.Id);

        // Assert
        var entries = await _repository.GetByUserIdAsync(_testUserId);
        entries.First().Id.Should().Be(entry1.Id);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_ShouldDeleteOldEntries()
    {
        // Arrange
        var oldEntry = CreateTestEntry("Old");
        oldEntry.CreatedAt = DateTime.UtcNow.AddDays(-40);
        await _repository.CreateAsync(oldEntry);

        var newEntry = CreateTestEntry("New");
        newEntry.CreatedAt = DateTime.UtcNow;
        await _repository.CreateAsync(newEntry);

        // Act
        var deletedCount = await _repository.CleanupOldEntriesAsync(30);

        // Assert
        deletedCount.Should().Be(1);
        var entries = await _repository.GetByUserIdAsync(_testUserId);
        entries.Should().HaveCount(1);
        entries.First().ContentPreview.Should().Be("New");
    }

    [Fact]
    public async Task GetCountByUserIdAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
        {
            await _repository.CreateAsync(CreateTestEntry($"Entry {i}"));
        }

        // Act
        var count = await _repository.GetCountByUserIdAsync(_testUserId);

        // Assert
        count.Should().Be(5);
    }

    [Fact]
    public async Task GetByHashAsync_ShouldFindByHash()
    {
        // Arrange
        var hash = "specific-hash";
        var entry = CreateTestEntry("Content", hash);
        await _repository.CreateAsync(entry);

        // Act
        var result = await _repository.GetByHashAsync(_testUserId, hash);

        // Assert
        result.Should().NotBeNull();
        result!.ContentHash.Should().Be(hash);
    }

    [Fact]
    public async Task GetByHashAsync_DifferentUser_ShouldNotFind()
    {
        // Arrange
        var hash = "unique-hash";
        var entry = CreateTestEntry("Content", hash);
        await _repository.CreateAsync(entry);

        // Act
        var result = await _repository.GetByHashAsync(Guid.NewGuid(), hash);

        // Assert
        result.Should().BeNull();
    }
}
