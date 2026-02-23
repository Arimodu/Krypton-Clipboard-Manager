using FluentAssertions;
using Google.Protobuf;
using Krypton.Shared.Protocol;

namespace Krypton_Desktop.Tests.Protocol;

public class ProtobufSerializationTests
{
    [Fact]
    public void ClipboardEntry_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var entry = new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString(),
            ContentType = ClipboardContentType.Text,
            Content = ByteString.CopyFromUtf8("Hello World"),
            ContentPreview = "Hello World",
            ContentHash = "abc123",
            SourceDevice = "TestDevice",
            CreatedAt = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act
        var bytes = entry.ToByteArray();
        var deserialized = ClipboardEntry.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Id.Should().Be(entry.Id);
        deserialized.ContentType.Should().Be(ClipboardContentType.Text);
        deserialized.Content.ToStringUtf8().Should().Be("Hello World");
        deserialized.ContentPreview.Should().Be("Hello World");
        deserialized.ContentHash.Should().Be("abc123");
        deserialized.SourceDevice.Should().Be("TestDevice");
    }

    [Fact]
    public void AuthLogin_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var request = new AuthLogin
        {
            Username = "testuser",
            Password = "testpass"
        };

        // Act
        var bytes = request.ToByteArray();
        var deserialized = AuthLogin.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Username.Should().Be("testuser");
        deserialized.Password.Should().Be("testpass");
    }

    [Fact]
    public void AuthResponse_Success_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var response = new AuthResponse
        {
            Success = true,
            UserId = Guid.NewGuid().ToString(),
            ApiKey = "kr_testkey123"
        };

        // Act
        var bytes = response.ToByteArray();
        var deserialized = AuthResponse.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Success.Should().BeTrue();
        deserialized.UserId.Should().Be(response.UserId);
        deserialized.ApiKey.Should().Be("kr_testkey123");
    }

    [Fact]
    public void AuthResponse_Failure_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var response = new AuthResponse
        {
            Success = false,
            Message = "Invalid credentials"
        };

        // Act
        var bytes = response.ToByteArray();
        var deserialized = AuthResponse.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Success.Should().BeFalse();
        deserialized.Message.Should().Be("Invalid credentials");
    }

    [Fact]
    public void ClipboardPush_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var push = new ClipboardPush
        {
            Entry = new ClipboardEntry
            {
                Id = Guid.NewGuid().ToString(),
                ContentType = ClipboardContentType.Text,
                Content = ByteString.CopyFromUtf8("Test content"),
                ContentPreview = "Test content",
                SourceDevice = "Desktop"
            }
        };

        // Act
        var bytes = push.ToByteArray();
        var deserialized = ClipboardPush.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Entry.ContentType.Should().Be(ClipboardContentType.Text);
        deserialized.Entry.Content.ToStringUtf8().Should().Be("Test content");
        deserialized.Entry.ContentPreview.Should().Be("Test content");
        deserialized.Entry.SourceDevice.Should().Be("Desktop");
    }

    [Fact]
    public void ClipboardHistory_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var history = new ClipboardHistory();
        history.Entries.Add(new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString(),
            ContentType = ClipboardContentType.Text,
            Content = ByteString.CopyFromUtf8("Entry 1"),
            ContentPreview = "Entry 1"
        });
        history.Entries.Add(new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString(),
            ContentType = ClipboardContentType.Text,
            Content = ByteString.CopyFromUtf8("Entry 2"),
            ContentPreview = "Entry 2"
        });

        // Act
        var bytes = history.ToByteArray();
        var deserialized = ClipboardHistory.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Entries.Should().HaveCount(2);
        deserialized.Entries[0].ContentPreview.Should().Be("Entry 1");
        deserialized.Entries[1].ContentPreview.Should().Be("Entry 2");
    }

    [Fact]
    public void ClipboardSearch_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var request = new ClipboardSearch
        {
            Query = "test query",
            Limit = 50
        };

        // Act
        var bytes = request.ToByteArray();
        var deserialized = ClipboardSearch.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Query.Should().Be("test query");
        deserialized.Limit.Should().Be(50);
    }

    [Fact]
    public void ClipboardContentType_AllTypes_ShouldSerialize()
    {
        // Arrange & Act & Assert
        var textEntry = new ClipboardEntry { ContentType = ClipboardContentType.Text };
        var imageEntry = new ClipboardEntry { ContentType = ClipboardContentType.Image };
        var fileEntry = new ClipboardEntry { ContentType = ClipboardContentType.File };

        ClipboardEntry.Parser.ParseFrom(textEntry.ToByteArray())
            .ContentType.Should().Be(ClipboardContentType.Text);
        ClipboardEntry.Parser.ParseFrom(imageEntry.ToByteArray())
            .ContentType.Should().Be(ClipboardContentType.Image);
        ClipboardEntry.Parser.ParseFrom(fileEntry.ToByteArray())
            .ContentType.Should().Be(ClipboardContentType.File);
    }

    [Fact]
    public void ClipboardEntry_LargeContent_ShouldSerialize()
    {
        // Arrange
        var largeContent = new string('x', 100_000); // 100KB of text
        var entry = new ClipboardEntry
        {
            Id = Guid.NewGuid().ToString(),
            ContentType = ClipboardContentType.Text,
            Content = ByteString.CopyFromUtf8(largeContent),
            ContentPreview = largeContent[..200]
        };

        // Act
        var bytes = entry.ToByteArray();
        var deserialized = ClipboardEntry.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Content.ToStringUtf8().Should().HaveLength(100_000);
    }

    [Fact]
    public void Heartbeat_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var heartbeat = new Heartbeat();

        // Act
        var bytes = heartbeat.ToByteArray();
        var deserialized = Heartbeat.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Should().NotBeNull();
    }

    [Fact]
    public void ErrorResponse_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var error = new ErrorResponse
        {
            Code = 404,
            Message = "Not found"
        };

        // Act
        var bytes = error.ToByteArray();
        var deserialized = ErrorResponse.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Code.Should().Be(404);
        deserialized.Message.Should().Be("Not found");
    }

    [Fact]
    public void KryptonPacket_WithPayload_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var packet = new KryptonPacket
        {
            Type = PacketType.AuthLogin,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceId = 1,
            AuthLogin = new AuthLogin
            {
                Username = "testuser",
                Password = "testpass"
            }
        };

        // Act
        var bytes = packet.ToByteArray();
        var deserialized = KryptonPacket.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Type.Should().Be(PacketType.AuthLogin);
        deserialized.AuthLogin.Username.Should().Be("testuser");
        deserialized.AuthLogin.Password.Should().Be("testpass");
    }

    [Fact]
    public void ClipboardBroadcast_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var broadcast = new ClipboardBroadcast
        {
            Entry = new ClipboardEntry
            {
                Id = Guid.NewGuid().ToString(),
                ContentType = ClipboardContentType.Text,
                Content = ByteString.CopyFromUtf8("Broadcast content"),
                ContentPreview = "Broadcast content"
            },
            FromDevice = "OtherDevice"
        };

        // Act
        var bytes = broadcast.ToByteArray();
        var deserialized = ClipboardBroadcast.Parser.ParseFrom(bytes);

        // Assert
        deserialized.Entry.ContentPreview.Should().Be("Broadcast content");
        deserialized.FromDevice.Should().Be("OtherDevice");
    }
}
