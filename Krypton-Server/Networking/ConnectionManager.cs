using System.Collections.Concurrent;
using Krypton.Shared.Protocol;

namespace Krypton.Server.Networking;

/// <summary>
/// Manages all active client connections.
/// </summary>
public class ConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public void Add(ClientConnection connection)
    {
        _connections.TryAdd(connection.Id, connection);
    }

    public void Remove(ClientConnection connection)
    {
        _connections.TryRemove(connection.Id, out _);
    }

    public void Remove(Guid connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public ClientConnection? Get(Guid connectionId)
    {
        _connections.TryGetValue(connectionId, out var connection);
        return connection;
    }

    public IEnumerable<ClientConnection> GetAll()
    {
        return _connections.Values;
    }

    public IEnumerable<ClientConnection> GetByUserId(Guid userId)
    {
        return _connections.Values
            .Where(c => c.AuthenticatedUser?.Id == userId);
    }

    public IEnumerable<ClientConnection> GetAuthenticatedConnections()
    {
        return _connections.Values.Where(c => c.IsAuthenticated);
    }

    /// <summary>
    /// Broadcast a packet to all authenticated connections except the sender.
    /// </summary>
    public async Task BroadcastAsync(
        PacketType type,
        byte[] payload,
        Guid? excludeConnectionId = null,
        Guid? onlyUserId = null,
        CancellationToken cancellationToken = default)
    {
        var connections = GetAuthenticatedConnections();

        if (excludeConnectionId.HasValue)
        {
            connections = connections.Where(c => c.Id != excludeConnectionId.Value);
        }

        if (onlyUserId.HasValue)
        {
            connections = connections.Where(c => c.AuthenticatedUser?.Id == onlyUserId.Value);
        }

        var tasks = connections.Select(async c =>
        {
            try
            {
                await c.SendPacketAsync(type, payload, cancellationToken);
            }
            catch
            {
                // Connection might be closed, ignore
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Broadcast a packet to all authenticated connections except the sender.
    /// </summary>
    public async Task BroadcastAsync<T>(
        PacketType type,
        T message,
        Guid? excludeConnectionId = null,
        Guid? onlyUserId = null,
        CancellationToken cancellationToken = default)
        where T : Google.Protobuf.IMessage
    {
        using var ms = new MemoryStream();
        using var cos = new Google.Protobuf.CodedOutputStream(ms);
        message.WriteTo(cos);
        cos.Flush();
        await BroadcastAsync(type, ms.ToArray(), excludeConnectionId, onlyUserId, cancellationToken);
    }

    /// <summary>
    /// Get connections that have been inactive for longer than the specified timeout.
    /// </summary>
    public IEnumerable<ClientConnection> GetStaleConnections(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        return _connections.Values.Where(c => c.LastActivityAt < cutoff);
    }

    public async Task DisconnectAllAsync()
    {
        var connections = _connections.Values.ToList();
        foreach (var connection in connections)
        {
            connection.Disconnect();
            await connection.DisposeAsync();
        }
        _connections.Clear();
    }
}
