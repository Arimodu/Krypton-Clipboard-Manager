using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Krypton.Shared.Protocol;
using Serilog;

namespace Krypton_Desktop.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Authenticating,
    Authenticated,
    Reconnecting
}

/// <summary>
/// Represents a queued clipboard entry to be sent when connection is restored.
/// </summary>
public record QueuedClipboardEntry(
    ClipboardContentType ContentType,
    byte[] Content,
    string Preview,
    DateTime QueuedAt);

public class ServerConnectionService : IDisposable
{
    private const int HeartbeatIntervalMs = 30000; // 30 seconds
    private const int MaxHeartbeatFailures = 3;
    private const int ReconnectDelayMs = 5000; // 5 seconds between reconnect attempts
    private const int MaxReconnectAttempts = 3;

    private readonly SettingsService _settingsService;
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private CancellationTokenSource? _connectionCts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private uint _sequenceId;
    private int _heartbeatFailures;
    private bool _wasEverConnected;
    private bool _isReconnecting;
    private string? _lastServerAddress;
    private int _lastServerPort;

    // Offline clipboard queue - no size limit
    private readonly ConcurrentQueue<QueuedClipboardEntry> _offlineQueue = new();

    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                var oldState = _state;
                _state = value;
                StateChanged?.Invoke(this, value);

                // Track if we've ever been connected
                if (value == ConnectionState.Authenticated)
                {
                    _wasEverConnected = true;
                }
            }
        }
    }

    public bool IsConnected => State == ConnectionState.Connected || State == ConnectionState.Authenticated;
    public bool IsAuthenticated => State == ConnectionState.Authenticated;
    public bool WasEverConnected => _wasEverConnected;
    public int QueuedEntriesCount => _offlineQueue.Count;
    public string? UserId { get; private set; }
    public string? AuthenticatedUsername { get; private set; }
    public bool IsAdmin { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastSyncTime { get; private set; }
    public ConnectionState CurrentState => State;

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<ClipboardEntry>? ClipboardBroadcastReceived;
    public event EventHandler<string>? ErrorReceived;
    /// <summary>
    /// Fired when connection is lost and reconnection failed after all retries.
    /// </summary>
    public event EventHandler<string>? ConnectionLost;
    /// <summary>
    /// Fired when connection is restored after being lost.
    /// </summary>
    public event EventHandler? ConnectionRestored;
    /// <summary>
    /// Fired when the server version is older than the client version.
    /// </summary>
    public event EventHandler<string>? ServerVersionMismatch;

    public ServerConnectionService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Settings;
        if (string.IsNullOrWhiteSpace(settings.ServerAddress))
        {
            LastError = "Server address not configured";
            return false;
        }

        return await ConnectAsync(settings.ServerAddress, settings.ServerPort, cancellationToken);
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Disconnected && State != ConnectionState.Reconnecting)
        {
            await DisconnectAsync();
        }

        State = ConnectionState.Connecting;
        LastError = null;

        try
        {
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _tcpClient = new TcpClient();

            Log.Information("Connecting to server {Host}:{Port}", host, port);

            await _tcpClient.ConnectAsync(host, port, _connectionCts.Token);

            Stream stream = _tcpClient.GetStream();

            // Wait for ServerHello (always sent in plain text)
            var serverHelloPacket = await PacketSerializer.ReadPacketAsync(stream, _connectionCts.Token);
            if (serverHelloPacket?.Type != PacketType.ServerHello)
            {
                throw new InvalidOperationException($"Expected ServerHello, got {serverHelloPacket?.Type}");
            }

            var serverHello = ServerHello.Parser.ParseFrom(serverHelloPacket.Value.Payload);
            Log.Debug("Server version: {Version}, TLS available: {TlsAvailable}, TLS required: {TlsRequired}",
                serverHello.ServerVersion, serverHello.TlsAvailable, serverHello.TlsRequired);

            // Decide whether to use TLS
            bool useTls = serverHello.TlsAvailable;

            if (useTls)
            {
                // Request TLS upgrade
                var startTls = new StartTls();
                using var ms = new MemoryStream();
                using var cos = new CodedOutputStream(ms);
                startTls.WriteTo(cos);
                cos.Flush();
                await PacketSerializer.WritePacketAsync(stream, PacketType.StartTls, ms.ToArray(), _connectionCts.Token);

                // Wait for StartTlsAck
                var startTlsAckPacket = await PacketSerializer.ReadPacketAsync(stream, _connectionCts.Token);
                if (startTlsAckPacket?.Type != PacketType.StartTlsAck)
                {
                    throw new InvalidOperationException($"Expected StartTlsAck, got {startTlsAckPacket?.Type}");
                }

                var startTlsAck = StartTlsAck.Parser.ParseFrom(startTlsAckPacket.Value.Payload);
                if (!startTlsAck.Success)
                {
                    if (serverHello.TlsRequired)
                    {
                        throw new InvalidOperationException($"TLS required but upgrade failed: {startTlsAck.Message}");
                    }
                    Log.Warning("TLS upgrade failed: {Message}, continuing without TLS", startTlsAck.Message);
                    useTls = false;
                }
                else
                {
                    // Upgrade to TLS
                    var sslStream = new SslStream(
                        stream,
                        leaveInnerStreamOpen: false,
                        ValidateServerCertificate);

                    await sslStream.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = host,
                            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                                  System.Security.Authentication.SslProtocols.Tls13
                        },
                        _connectionCts.Token);

                    stream = sslStream;
                    Log.Debug("TLS connection established");
                }
            }
            else if (serverHello.TlsRequired)
            {
                throw new InvalidOperationException("Server requires TLS but it's not available");
            }

            _stream = stream;

            // Send Connect packet
            var connectPacket = new Connect
            {
                ClientVersion = PacketConstants.ClientVersion,
                Platform = GetPlatformString(),
                DeviceId = GetDeviceId(),
                DeviceName = Environment.MachineName
            };

            await SendPacketAsync(PacketType.Connect, connectPacket, _connectionCts.Token);

            // Wait for ConnectAck
            var response = await ReadPacketAsync(_connectionCts.Token);
            if (response?.Type != PacketType.ConnectAck)
            {
                throw new InvalidOperationException($"Expected ConnectAck, got {response?.Type}");
            }

            var connectAck = ConnectAck.Parser.ParseFrom(response.Value.Payload);
            var serverBase = connectAck.ServerVersion.Split('+')[0];
            var clientBase = PacketConstants.AppVersion;
            if (System.Version.TryParse(serverBase, out var sv) &&
                System.Version.TryParse(clientBase, out var cv) && cv > sv)
            {
                ServerVersionMismatch?.Invoke(this,
                    $"Server is v{serverBase}. Client is v{clientBase}. Please upgrade the server.");
            }

            State = ConnectionState.Connected;

            // Save connection info for reconnection
            _lastServerAddress = host;
            _lastServerPort = port;
            _heartbeatFailures = 0;

            // Start receive loop and heartbeat loop
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token), _connectionCts.Token);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_connectionCts.Token), _connectionCts.Token);

            Log.Information("Connected to server {Host}:{Port} (TLS: {TlsEnabled})", host, port, useTls);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to server");
            LastError = ex.Message;
            State = ConnectionState.Disconnected;
            await CleanupConnectionAsync();
            return false;
        }
    }

    public async Task<bool> AuthenticateWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            LastError = "Not connected to server";
            return false;
        }

        State = ConnectionState.Authenticating;

        try
        {
            var authPacket = new AuthApiKey { ApiKey = apiKey };
            await SendPacketAsync(PacketType.AuthApiKey, authPacket, cancellationToken);

            var response = await WaitForResponseAsync(PacketType.AuthResponse, cancellationToken);
            if (response == null)
            {
                LastError = "No response from server";
                State = ConnectionState.Connected;
                return false;
            }

            var authResponse = AuthResponse.Parser.ParseFrom(response.Value.Payload);
            if (authResponse.Success)
            {
                UserId = authResponse.UserId;
                AuthenticatedUsername = "API Key User";
                IsAdmin = authResponse.IsAdmin;
                State = ConnectionState.Authenticated;
                LastSyncTime = DateTime.Now;
                Log.Information("Authenticated with API key");
                return true;
            }

            LastError = authResponse.Message;
            State = ConnectionState.Connected;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Authentication failed");
            LastError = ex.Message;
            State = ConnectionState.Connected;
            return false;
        }
    }

    public async Task<(bool Success, string? ApiKey, string? Error)> AuthenticateWithPasswordAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return (false, null, "Not connected to server");
        }

        State = ConnectionState.Authenticating;

        try
        {
            var authPacket = new AuthLogin
            {
                Username = username,
                Password = password
            };
            await SendPacketAsync(PacketType.AuthLogin, authPacket, cancellationToken);

            var response = await WaitForResponseAsync(PacketType.AuthResponse, cancellationToken);
            if (response == null)
            {
                State = ConnectionState.Connected;
                return (false, null, "No response from server");
            }

            var authResponse = AuthResponse.Parser.ParseFrom(response.Value.Payload);
            if (authResponse.Success)
            {
                UserId = authResponse.UserId;
                AuthenticatedUsername = username;
                IsAdmin = authResponse.IsAdmin;
                State = ConnectionState.Authenticated;
                LastSyncTime = DateTime.Now;
                Log.Information("Authenticated as {Username}", username);
                return (true, authResponse.ApiKey, null);
            }

            State = ConnectionState.Connected;
            return (false, null, authResponse.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Authentication failed");
            State = ConnectionState.Connected;
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string? ApiKey, string? Error)> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return (false, null, "Not connected to server");
        }

        State = ConnectionState.Authenticating;

        try
        {
            var registerPacket = new AuthRegister
            {
                Username = username,
                Password = password
            };
            await SendPacketAsync(PacketType.AuthRegister, registerPacket, cancellationToken);

            var response = await WaitForResponseAsync(PacketType.AuthResponse, cancellationToken);
            if (response == null)
            {
                State = ConnectionState.Connected;
                return (false, null, "No response from server");
            }

            var authResponse = AuthResponse.Parser.ParseFrom(response.Value.Payload);
            if (authResponse.Success)
            {
                UserId = authResponse.UserId;
                AuthenticatedUsername = username;
                IsAdmin = authResponse.IsAdmin;
                State = ConnectionState.Authenticated;
                LastSyncTime = DateTime.Now;
                Log.Information("Registered and authenticated as {Username}", username);
                return (true, authResponse.ApiKey, null);
            }

            State = ConnectionState.Connected;
            return (false, null, authResponse.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Registration failed");
            State = ConnectionState.Connected;
            return (false, null, ex.Message);
        }
    }

    public async Task<bool> PushClipboardEntryAsync(
        ClipboardContentType contentType,
        byte[] content,
        string preview,
        CancellationToken cancellationToken = default)
    {
        // If we're authenticated, send immediately
        if (IsAuthenticated)
        {
            return await PushClipboardEntryInternalAsync(contentType, content, preview, cancellationToken);
        }

        // If we were ever connected, queue for later
        if (_wasEverConnected)
        {
            _offlineQueue.Enqueue(new QueuedClipboardEntry(contentType, content, preview, DateTime.UtcNow));
            Log.Debug("Queued clipboard entry for later (queue size: {Count})", _offlineQueue.Count);
            return true;
        }

        // Never been connected, just log and return
        Log.Warning("Cannot push clipboard entry: not authenticated and never connected");
        return false;
    }

    private async Task<bool> PushClipboardEntryInternalAsync(
        ClipboardContentType contentType,
        byte[] content,
        string preview,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new ClipboardEntry
            {
                ContentType = contentType,
                Content = ByteString.CopyFrom(content),
                ContentPreview = preview,
                CreatedAt = PacketSerializer.GetTimestamp(),
                SourceDevice = GetDeviceId()
            };

            var pushPacket = new ClipboardPush { Entry = entry };
            await SendPacketAsync(PacketType.ClipboardPush, pushPacket, cancellationToken);

            Log.Debug("Pushed clipboard entry to server");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to push clipboard entry");
            return false;
        }
    }

    public async Task<(ClipboardEntry[] Entries, bool HasMore, int TotalCount)?> PullHistoryAsync(
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            Log.Warning("Cannot pull history: not authenticated");
            return null;
        }

        try
        {
            var pullPacket = new ClipboardPull { Limit = limit, Offset = offset };
            await SendPacketAsync(PacketType.ClipboardPull, pullPacket, cancellationToken);

            var response = await WaitForResponseAsync(PacketType.ClipboardHistory, cancellationToken);
            if (response == null)
            {
                Log.Warning("No history response from server");
                return null;
            }

            var history = ClipboardHistory.Parser.ParseFrom(response.Value.Payload);
            Log.Debug("Pulled {Count} entries from server (offset={Offset}, hasMore={HasMore})",
                history.Entries.Count, offset, history.HasMore);
            return (history.Entries.ToArray(), history.HasMore, history.TotalCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to pull history");
            return null;
        }
    }

    public async Task<ClipboardEntry[]?> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            Log.Warning("Cannot search: not authenticated");
            return null;
        }

        try
        {
            var searchPacket = new ClipboardSearch
            {
                Query = query,
                Limit = limit
            };
            await SendPacketAsync(PacketType.ClipboardSearch, searchPacket, cancellationToken);

            var response = await WaitForResponseAsync(PacketType.ClipboardSearchResult, cancellationToken);
            if (response == null)
            {
                Log.Warning("No search response from server");
                return null;
            }

            var result = ClipboardSearchResult.Parser.ParseFrom(response.Value.Payload);
            Log.Debug("Search returned {Count} results", result.Entries.Count);
            return result.Entries.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to search");
            return null;
        }
    }

    public async Task<bool> MoveToTopAsync(string entryId, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
            return false;

        try
        {
            var packet = new ClipboardMoveToTop { EntryId = entryId };
            await SendPacketAsync(PacketType.ClipboardMoveToTop, packet, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to move entry to top");
            return false;
        }
    }

    public async Task<bool> DeleteEntryAsync(string entryId, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
            return false;

        try
        {
            var packet = new ClipboardDelete { EntryId = entryId };
            await SendPacketAsync(PacketType.ClipboardDelete, packet, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete entry");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Disconnected)
            return;

        try
        {
            if (_stream != null && State != ConnectionState.Connecting)
            {
                var disconnectPacket = new Disconnect { Reason = "Client disconnect" };
                await SendPacketAsync(PacketType.Disconnect, disconnectPacket, CancellationToken.None);
            }
        }
        catch
        {
            // Ignore errors during disconnect
        }

        await CleanupConnectionAsync();
        State = ConnectionState.Disconnected;
        Log.Information("Disconnected from server");
    }

    private async Task SendPacketAsync<T>(PacketType type, T message, CancellationToken cancellationToken)
        where T : IMessage
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var ms = new MemoryStream();
            using var cos = new CodedOutputStream(ms);
            message.WriteTo(cos);
            cos.Flush();

            await PacketSerializer.WritePacketAsync(_stream, type, ms.ToArray(), cancellationToken);
            _sequenceId++;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(PacketType Type, byte[] Payload)?> ReadPacketAsync(CancellationToken cancellationToken)
    {
        if (_stream == null)
            return null;

        return await PacketSerializer.ReadPacketAsync(_stream, cancellationToken);
    }

    private TaskCompletionSource<(PacketType Type, byte[] Payload)>? _pendingResponse;
    private PacketType _expectedResponseType;

    private async Task<(PacketType Type, byte[] Payload)?> WaitForResponseAsync(
        PacketType expectedType,
        CancellationToken cancellationToken,
        int timeoutMs = 10000)
    {
        _expectedResponseType = expectedType;
        _pendingResponse = new TaskCompletionSource<(PacketType Type, byte[] Payload)>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            using var registration = cts.Token.Register(() => _pendingResponse.TrySetCanceled());
            return await _pendingResponse.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingResponse = null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                var packet = await ReadPacketAsync(cancellationToken);
                if (packet == null)
                {
                    Log.Warning("Connection closed by server");
                    break;
                }

                await HandlePacketAsync(packet.Value.Type, packet.Value.Payload);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in receive loop");
        }
        finally
        {
            if (State != ConnectionState.Disconnected)
            {
                State = ConnectionState.Disconnected;
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                await Task.Delay(HeartbeatIntervalMs, cancellationToken);

                if (!IsConnected)
                    break;

                try
                {
                    var heartbeat = new Heartbeat();
                    await SendPacketAsync(PacketType.Heartbeat, heartbeat, cancellationToken);
                    Log.Debug("Sent heartbeat to server");

                    // Reset failure counter on success
                    _heartbeatFailures = 0;
                }
                catch (Exception ex)
                {
                    _heartbeatFailures++;
                    Log.Warning(ex, "Failed to send heartbeat ({Failures}/{Max})",
                        _heartbeatFailures, MaxHeartbeatFailures);

                    if (_heartbeatFailures >= MaxHeartbeatFailures)
                    {
                        Log.Warning("Too many heartbeat failures, attempting reconnection");
                        _ = Task.Run(() => AttemptReconnectionAsync());
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in heartbeat loop");
        }
    }

    /// <summary>
    /// Attempts to reconnect to the server after connection loss.
    /// </summary>
    private async Task AttemptReconnectionAsync()
    {
        if (_isReconnecting || string.IsNullOrEmpty(_lastServerAddress))
            return;

        _isReconnecting = true;
        var apiKey = _settingsService.Settings.ApiKey;

        try
        {
            // Clean up existing connection
            await CleanupConnectionAsync();
            State = ConnectionState.Reconnecting;

            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                Log.Information("Reconnection attempt {Attempt}/{Max}", attempt, MaxReconnectAttempts);

                try
                {
                    var connected = await ConnectAsync(_lastServerAddress, _lastServerPort);
                    if (connected && !string.IsNullOrEmpty(apiKey))
                    {
                        var authenticated = await AuthenticateWithApiKeyAsync(apiKey);
                        if (authenticated)
                        {
                            Log.Information("Reconnection successful");
                            ConnectionRestored?.Invoke(this, EventArgs.Empty);

                            // Flush the offline queue
                            await FlushOfflineQueueAsync();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Reconnection attempt {Attempt} failed", attempt);
                }

                if (attempt < MaxReconnectAttempts)
                {
                    await Task.Delay(ReconnectDelayMs);
                }
            }

            // All reconnection attempts failed
            Log.Error("All reconnection attempts failed");
            State = ConnectionState.Disconnected;
            ConnectionLost?.Invoke(this, "Unable to reconnect to server after multiple attempts");
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    /// <summary>
    /// Flushes queued clipboard entries to the server.
    /// </summary>
    private async Task FlushOfflineQueueAsync()
    {
        if (_offlineQueue.IsEmpty)
            return;

        Log.Information("Flushing {Count} queued clipboard entries", _offlineQueue.Count);

        while (_offlineQueue.TryDequeue(out var entry))
        {
            try
            {
                await PushClipboardEntryInternalAsync(entry.ContentType, entry.Content, entry.Preview);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to send queued clipboard entry");
                // Re-queue if we're no longer connected
                if (!IsAuthenticated)
                {
                    _offlineQueue.Enqueue(entry);
                    break;
                }
            }
        }
    }

    private Task HandlePacketAsync(PacketType type, byte[] payload)
    {
        // Check if this is an expected response
        if (_pendingResponse != null && type == _expectedResponseType)
        {
            _pendingResponse.TrySetResult((type, payload));
            return Task.CompletedTask;
        }

        switch (type)
        {
            case PacketType.HeartbeatAck:
                // Heartbeat response, ignore
                break;

            case PacketType.ClipboardBroadcast:
                var broadcast = ClipboardBroadcast.Parser.ParseFrom(payload);
                ClipboardBroadcastReceived?.Invoke(this, broadcast.Entry);
                Log.Debug("Received clipboard broadcast from {Device}", broadcast.FromDevice);
                break;

            case PacketType.ClipboardPushAck:
                // Push acknowledged
                Log.Debug("Clipboard push acknowledged");
                break;

            case PacketType.Error:
                var error = ErrorResponse.Parser.ParseFrom(payload);
                ErrorReceived?.Invoke(this, error.Message);
                Log.Warning("Server error: {Message}", error.Message);
                break;

            default:
                Log.Debug("Received unexpected packet type: {Type}", type);
                break;
        }

        return Task.CompletedTask;
    }

    private async Task CleanupConnectionAsync()
    {
        _connectionCts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore
            }
            _receiveTask = null;
        }

        if (_heartbeatTask != null)
        {
            try
            {
                await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore
            }
            _heartbeatTask = null;
        }

        if (_stream != null)
        {
            try
            {
                await _stream.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        _connectionCts?.Dispose();
        _connectionCts = null;

        UserId = null;
        AuthenticatedUsername = null;
        IsAdmin = false;
    }

    private static bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // In development, accept self-signed certificates
        // TODO: Add proper certificate validation for production
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        Log.Warning("TLS certificate validation error: {Errors}", sslPolicyErrors);

        // Accept self-signed for now
        return true;
    }

    private static string GetPlatformString()
    {
        if (OperatingSystem.IsWindows())
            return "Windows";
        if (OperatingSystem.IsMacOS())
            return "macOS";
        if (OperatingSystem.IsLinux())
            return "Linux";
        return "Unknown";
    }

    private static string GetDeviceId()
    {
        // Generate a stable device ID based on machine name
        return Environment.MachineName;
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
    }
}
