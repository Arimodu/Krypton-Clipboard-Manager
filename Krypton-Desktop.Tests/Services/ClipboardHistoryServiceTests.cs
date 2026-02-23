using FluentAssertions;
using Krypton_Desktop.Models;
using Krypton_Desktop.Services;
using Krypton.Shared.Protocol;

namespace Krypton_Desktop.Tests.Services;

public class ClipboardHistoryServiceTests
{
    private ClipboardHistoryService CreateService(int maxItems = 100)
    {
        var service = new ClipboardHistoryService { MaxItems = maxItems };
        return service;
    }

    private static ClipboardItem CreateTestItem(string text)
    {
        var item = ClipboardItem.FromText(text);
        item.Id = Guid.NewGuid();
        return item;
    }

    [Fact]
    public void Add_ShouldAddItemToHistory()
    {
        // Arrange
        var service = CreateService();
        var item = CreateTestItem("Hello World");

        // Act
        service.Add(item);

        // Assert
        service.Count.Should().Be(1);
        service.GetMostRecent().Should().NotBeNull();
        service.GetMostRecent()!.TextContent.Should().Be("Hello World");
    }

    [Fact]
    public void Add_ShouldInsertAtBeginning()
    {
        // Arrange
        var service = CreateService();
        var item1 = CreateTestItem("First");
        var item2 = CreateTestItem("Second");

        // Act
        service.Add(item1);
        service.Add(item2);

        // Assert
        service.GetMostRecent()!.TextContent.Should().Be("Second");
    }

    [Fact]
    public void Add_DuplicateHash_ShouldMoveToTop()
    {
        // Arrange
        var service = CreateService();
        var item1 = CreateTestItem("First");
        var item2 = CreateTestItem("Second");
        var item3 = CreateTestItem("First"); // Same content as item1

        // Act
        service.Add(item1);
        service.Add(item2);
        service.Add(item3);

        // Assert
        service.Count.Should().Be(2); // Duplicate not added as new
        service.GetMostRecent()!.TextContent.Should().Be("First"); // First moved to top
    }

    [Fact]
    public void Add_ShouldTrimHistoryWhenExceedingMax()
    {
        // Arrange
        var service = CreateService(maxItems: 3);

        // Act
        for (int i = 1; i <= 5; i++)
        {
            service.Add(CreateTestItem($"Item {i}"));
        }

        // Assert
        service.Count.Should().Be(3);
        var items = service.GetAll();
        items[0].TextContent.Should().Be("Item 5");
        items[1].TextContent.Should().Be("Item 4");
        items[2].TextContent.Should().Be("Item 3");
    }

    [Fact]
    public void GetAll_ShouldReturnItemsInOrder()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("First"));
        service.Add(CreateTestItem("Second"));
        service.Add(CreateTestItem("Third"));

        // Act
        var items = service.GetAll();

        // Assert
        items.Should().HaveCount(3);
        items[0].TextContent.Should().Be("Third");
        items[1].TextContent.Should().Be("Second");
        items[2].TextContent.Should().Be("First");
    }

    [Fact]
    public void GetMostRecent_EmptyHistory_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetMostRecent();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MoveToTop_ShouldReorderItem()
    {
        // Arrange
        var service = CreateService();
        var item1 = CreateTestItem("First");
        var item2 = CreateTestItem("Second");
        var item3 = CreateTestItem("Third");
        service.Add(item1);
        service.Add(item2);
        service.Add(item3);

        // Act
        service.MoveToTop(item1.Id);

        // Assert
        service.GetMostRecent()!.Id.Should().Be(item1.Id);
    }

    [Fact]
    public void Remove_ShouldDeleteItem()
    {
        // Arrange
        var service = CreateService();
        var item = CreateTestItem("Test");
        service.Add(item);

        // Act
        var result = service.Remove(item.Id);

        // Assert
        result.Should().BeTrue();
        service.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_NonExistent_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.Remove(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Clear_ShouldRemoveAllItems()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("First"));
        service.Add(CreateTestItem("Second"));

        // Act
        service.Clear();

        // Assert
        service.Count.Should().Be(0);
    }

    [Fact]
    public void Search_ShouldFindMatchingItems()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("Hello World"));
        service.Add(CreateTestItem("Goodbye World"));
        service.Add(CreateTestItem("Hello Universe"));

        // Act
        var results = service.Search("Hello");

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Preview.Should().Contain("Hello"));
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("HELLO World"));

        // Act
        var results = service.Search("hello");

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Search_EmptyQuery_ShouldReturnAll()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("First"));
        service.Add(CreateTestItem("Second"));

        // Act
        var results = service.Search("");

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public void AddFromServer_ShouldMarkAsSynced()
    {
        // Arrange
        var service = CreateService();
        var item = CreateTestItem("Server Item");

        // Act
        service.AddFromServer(item);

        // Assert
        var result = service.GetMostRecent();
        result.Should().NotBeNull();
        result!.IsSynced.Should().BeTrue();
    }

    [Fact]
    public void MaxItems_Setter_ShouldTrimHistory()
    {
        // Arrange
        var service = CreateService(maxItems: 10);
        for (int i = 1; i <= 10; i++)
        {
            service.Add(CreateTestItem($"Item {i}"));
        }

        // Act
        service.MaxItems = 5;

        // Assert
        service.Count.Should().Be(5);
    }

    [Fact]
    public void ItemAdded_Event_ShouldFire()
    {
        // Arrange
        var service = CreateService();
        ClipboardItem? receivedItem = null;
        service.ItemAdded += (_, item) => receivedItem = item;

        // Act
        var item = CreateTestItem("Test");
        service.Add(item);

        // Assert
        receivedItem.Should().NotBeNull();
        receivedItem!.TextContent.Should().Be("Test");
    }

    [Fact]
    public void HistoryCleared_Event_ShouldFire()
    {
        // Arrange
        var service = CreateService();
        service.Add(CreateTestItem("Test"));
        var eventFired = false;
        service.HistoryCleared += (_, _) => eventFired = true;

        // Act
        service.Clear();

        // Assert
        eventFired.Should().BeTrue();
    }
}
