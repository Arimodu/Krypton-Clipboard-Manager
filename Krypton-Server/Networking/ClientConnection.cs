using System.Net.Security;
using System.Net.Sockets;
using Krypton.Server.Configuration;
using Krypton.Server.Database.Entities;
using Krypton.Shared.Protocol;
using Serilog;

namespace Krypton.Server.Networking;

/// <summary>
/// Represents a connected client with optional TLS support.
/// </summary>
public class ClientConnection : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _disconnectCts = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string RemoteEndPoint { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;
    public bool IsAuthenticated => AuthenticatedUser != null;
    public User? AuthenticatedUser { get; private set; }
    public string? DeviceName { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool IsTlsEnabled { get; private set; }

    public CancellationToken DisconnectToken => _disconnectCts.Token;

    private ClientConnection(TcpClient tcpClient, Stream stream, bool isTls)
    {
        _tcpClient = tcpClient;
        _stream = stream;
        IsTlsEnabled = isTls;
        RemoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Creates a new client connection with capability handshake.
    /// Server sends ServerHello first, then client decides whether to use TLS.
    /// </summary>
    public static async Task<ClientConnection> CreateAsync(
        TcpClient tcpClient,
        SslServerAuthenticationOptions? sslOptions,
        TlsMode tlsMode,
        CancellationToken cancellationToken)
    {
        Stream stream = tcpClient.GetStream();
        var tlsAvailable = sslOptions != null && tlsMode != TlsMode.Off;
        var tlsRequired = tlsMode == TlsMode.Required;

        // Send ServerHello in plain text
        var serverHello = new ServerHello
        {
            ServerVersion = PacketConstants.FullVersion,
            TlsAvailable = tlsAvailable,
            TlsRequired = tlsRequired
        };

        await PacketSerializer.WritePacketAsync(
            stream,
            PacketType.ServerHello,
            SerializeMessage(serverHello),
            cancellationToken);

        // Wait for client response
        var response = await PacketSerializer.ReadPacketAsync(stream, cancellationToken) ?? throw new InvalidOperationException("Client disconnected during handshake");
        bool isTls = false;

        // Check if client wants TLS
        if (response.Type == PacketType.StartTls)
        {
            if (tlsAvailable && sslOptions != null)
            {
                // Send StartTlsAck
                var ack = new StartTlsAck { Success = true, Message = "Upgrading to TLS" };
                await PacketSerializer.WritePacketAsync(
                    stream,
                    PacketType.StartTlsAck,
                    SerializeMessage(ack),
                    cancellationToken);

                // Upgrade to TLS
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
                stream = sslStream;
                isTls = true;
            }
            else
            {
                // TLS not available
                var ack = new StartTlsAck { Success = false, Message = "TLS not available" };
                await PacketSerializer.WritePacketAsync(
                    stream,
                    PacketType.StartTlsAck,
                    SerializeMessage(ack),
                    cancellationToken);

                if (tlsRequired)
                {
                    throw new InvalidOperationException("TLS required but not available");
                }
            }
        }
        // If client sent Connect directly, continue without TLS (response will be handled by PacketHandler)

        return new ClientConnection(tcpClient, stream, isTls);
    }

    /// <summary>
    /// Gets the first packet received during handshake (if it was Connect, not StartTls).
    /// This is stored so PacketHandler can process it.
    /// </summary>
    public (PacketType Type, byte[] Payload)? InitialPacket { get; private set; }

    /// <summary>
    /// Creates a new client connection with capability handshake.
    /// Returns the connection and any initial packet that needs processing.
    /// </summary>
    public static async Task<(ClientConnection Connection, (PacketType Type, byte[] Payload)? InitialPacket)> CreateWithHandshakeAsync(
        TcpClient tcpClient,
        SslServerAuthenticationOptions? sslOptions,
        TlsMode tlsMode,
        CancellationToken cancellationToken)
    {
        var remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Stream stream = tcpClient.GetStream();
        var tlsAvailable = sslOptions != null && tlsMode != TlsMode.Off;
        var tlsRequired = tlsMode == TlsMode.Required;

        // Send ServerHello in plain text
        var serverHello = new ServerHello
        {
            ServerVersion = PacketConstants.FullVersion,
            TlsAvailable = tlsAvailable,
            TlsRequired = tlsRequired
        };

        var serverHelloPayload = SerializeMessage(serverHello);
        await PacketSerializer.WritePacketAsync(
            stream,
            PacketType.ServerHello,
            serverHelloPayload,
            cancellationToken);
        Log.Debug("Sent packet: Type={PacketType}, Size={Size} bytes, To={RemoteEndPoint}",
            PacketType.ServerHello, serverHelloPayload.Length, remoteEndPoint);

        // Wait for client response
        var response = await PacketSerializer.ReadPacketAsync(stream, cancellationToken);
        if (response == null)
        {
            throw new InvalidOperationException("Client disconnected during handshake");
        }
        Log.Debug("Received packet: Type={PacketType}, Size={Size} bytes, From={RemoteEndPoint}",
            response.Value.Type, response.Value.Payload.Length, remoteEndPoint);

        bool isTls = false;
        (PacketType Type, byte[] Payload)? initialPacket = null;

        // Check if client wants TLS
        if (response.Value.Type == PacketType.StartTls)
        {
            if (tlsAvailable && sslOptions != null)
            {
                // Send StartTlsAck
                var ack = new StartTlsAck { Success = true, Message = "Upgrading to TLS" };
                var ackPayload = SerializeMessage(ack);
                await PacketSerializer.WritePacketAsync(
                    stream,
                    PacketType.StartTlsAck,
                    ackPayload,
                    cancellationToken);
                Log.Debug("Sent packet: Type={PacketType}, Size={Size} bytes, To={RemoteEndPoint}",
                    PacketType.StartTlsAck, ackPayload.Length, remoteEndPoint);

                // Upgrade to TLS
                Log.Debug("Upgrading connection to TLS for {RemoteEndPoint}", remoteEndPoint);
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);
                stream = sslStream;
                isTls = true;
                Log.Debug("TLS handshake completed for {RemoteEndPoint}", remoteEndPoint);
            }
            else
            {
                // TLS not available
                var ack = new StartTlsAck { Success = false, Message = "TLS not available" };
                var ackPayload = SerializeMessage(ack);
                await PacketSerializer.WritePacketAsync(
                    stream,
                    PacketType.StartTlsAck,
                    ackPayload,
                    cancellationToken);
                Log.Debug("Sent packet: Type={PacketType}, Size={Size} bytes, To={RemoteEndPoint}",
                    PacketType.StartTlsAck, ackPayload.Length, remoteEndPoint);

                if (tlsRequired)
                {
                    throw new InvalidOperationException("TLS required but not available");
                }
            }
        }
        else
        {
            // Client sent something else (like Connect) - store it for later processing
            initialPacket = response.Value;
        }

        var connection = new ClientConnection(tcpClient, stream, isTls);
        return (connection, initialPacket);
    }

    private static byte[] SerializeMessage<T>(T message) where T : Google.Protobuf.IMessage
    {
        using var ms = new MemoryStream();
        using var cos = new Google.Protobuf.CodedOutputStream(ms);
        message.WriteTo(cos);
        cos.Flush();
        return ms.ToArray();
    }

    public void SetDeviceInfo(string? deviceName)
    {
        DeviceName = deviceName;
    }

    public void SetAuthenticated(User user)
    {
        AuthenticatedUser = user;
        UpdateActivity();
    }

    public void UpdateActivity()
    {
        LastActivityAt = DateTime.UtcNow;
    }

    public async Task<(PacketType Type, byte[] Payload)?> ReadPacketAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disconnectCts.Token);

            var result = await PacketSerializer.ReadPacketAsync(_stream, linkedCts.Token);
            if (result.HasValue)
            {
                Log.Debug("Received packet: Type={PacketType}, Size={Size} bytes, From={RemoteEndPoint}",
                    result.Value.Type, result.Value.Payload.Length, RemoteEndPoint);
                UpdateActivity();
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SendPacketAsync(PacketType type, byte[] payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disconnectCts.Token);

            await PacketSerializer.WritePacketAsync(_stream, type, payload, linkedCts.Token);
            Log.Debug("Sent packet: Type={PacketType}, Size={Size} bytes, To={RemoteEndPoint}",
                type, payload.Length, RemoteEndPoint);
            UpdateActivity();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendPacketAsync<T>(PacketType type, T message, CancellationToken cancellationToken)
        where T : Google.Protobuf.IMessage
    {
        await SendPacketAsync(type, SerializeMessage(message), cancellationToken);
    }

    public void Disconnect()
    {
        _disconnectCts.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _disconnectCts.Cancel();

        try
        {
            await _stream.DisposeAsync();
        }
        catch { }

        try
        {
            _tcpClient.Dispose();
        }
        catch { }

        _disconnectCts.Dispose();
        _writeLock.Dispose();
    }
}
