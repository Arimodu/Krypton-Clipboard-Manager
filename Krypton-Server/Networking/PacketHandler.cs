using Google.Protobuf;
using Krypton.Server.Database.Repositories;
using Krypton.Server.Services;
using Krypton.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace Krypton.Server.Networking;

/// <summary>
/// Handles incoming packets and dispatches them to appropriate services.
/// </summary>
public class PacketHandler : IPacketHandler
{
    private readonly AuthenticationService _authService;
    private readonly ClipboardService _clipboardService;
    private readonly ConnectionManager _connectionManager;
    private readonly ILogger<PacketHandler> _logger;
    private readonly IApiKeyRepository _apiKeyRepository;

    public PacketHandler(
        AuthenticationService authService,
        ClipboardService clipboardService,
        ConnectionManager connectionManager,
        ILogger<PacketHandler> logger,
        IApiKeyRepository apiKeyRepository)
    {
        _authService = authService;
        _clipboardService = clipboardService;
        _connectionManager = connectionManager;
        _logger = logger;
        _apiKeyRepository = apiKeyRepository;
    }

    public async Task HandleConnectionAsync(
        ClientConnection connection,
        (PacketType Type, byte[] Payload)? initialPacket,
        CancellationToken cancellationToken)
    {
        // Process the initial packet if one was received during handshake
        // (This happens when client skipped TLS and sent Connect directly)
        if (initialPacket.HasValue)
        {
            try
            {
                await HandlePacketAsync(connection, initialPacket.Value.Type, initialPacket.Value.Payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling initial packet {Type} from {Connection}",
                    initialPacket.Value.Type, connection.Id);

                await SendErrorAsync(connection, $"Error processing request: {ex.Message}", cancellationToken);
            }
        }

        // Continue processing packets
        while (!cancellationToken.IsCancellationRequested && !connection.DisconnectToken.IsCancellationRequested)
        {
            var packet = await connection.ReadPacketAsync(cancellationToken);
            if (packet == null)
            {
                break; // Connection closed
            }

            try
            {
                await HandlePacketAsync(connection, packet.Value.Type, packet.Value.Payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling packet {Type} from {Connection}",
                    packet.Value.Type, connection.Id);

                await SendErrorAsync(connection, $"Error processing request: {ex.Message}", cancellationToken);
            }
        }
    }

    private async Task HandlePacketAsync(
        ClientConnection connection,
        PacketType type,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case PacketType.Heartbeat:
                await HandleHeartbeatAsync(connection, cancellationToken);
                break;

            case PacketType.Connect:
                await HandleConnectAsync(connection, payload, cancellationToken);
                break;

            case PacketType.Disconnect:
                connection.Disconnect();
                break;

            case PacketType.AuthLogin:
                await HandleAuthLoginAsync(connection, payload, cancellationToken);
                break;

            case PacketType.AuthRegister:
                await HandleAuthRegisterAsync(connection, payload, cancellationToken);
                break;

            case PacketType.AuthApiKey:
                await HandleAuthApiKeyAsync(connection, payload, cancellationToken);
                break;

            case PacketType.AuthLogout:
                await HandleAuthLogoutAsync(connection, cancellationToken);
                break;

            case PacketType.ClipboardPush:
                await HandleClipboardPushAsync(connection, payload, cancellationToken);
                break;

            case PacketType.ClipboardPull:
                await HandleClipboardPullAsync(connection, payload, cancellationToken);
                break;

            case PacketType.ClipboardSearch:
                await HandleClipboardSearchAsync(connection, payload, cancellationToken);
                break;

            case PacketType.ClipboardMoveToTop:
                await HandleClipboardMoveToTopAsync(connection, payload, cancellationToken);
                break;

            case PacketType.ClipboardDelete:
                await HandleClipboardDeleteAsync(connection, payload, cancellationToken);
                break;

            default:
                _logger.LogWarning("Unknown packet type {Type} from {Connection}", type, connection.Id);
                await SendErrorAsync(connection, $"Unknown packet type: {type}", cancellationToken);
                break;
        }
    }

    private async Task HandleHeartbeatAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        var ack = new HeartbeatAck();
        await connection.SendPacketAsync(PacketType.HeartbeatAck, ack, cancellationToken);
    }

    private async Task HandleConnectAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var request = Connect.Parser.ParseFrom(payload);

        // Store device info for later use (e.g., API key naming)
        connection.SetDeviceInfo(request.DeviceName);

        _logger.LogDebug("Client {Id} connecting: version={Version}, platform={Platform}, device={DeviceName}",
            connection.Id, request.ClientVersion, request.Platform, request.DeviceName);

        var response = new ConnectAck
        {
            ServerVersion = PacketConstants.FullVersion,
            RequiresAuth = true
        };

        await connection.SendPacketAsync(PacketType.ConnectAck, response, cancellationToken);
    }

    private async Task HandleAuthLoginAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var request = AuthLogin.Parser.ParseFrom(payload);
        var result = await _authService.AuthenticateWithPasswordAsync(request.Username, request.Password);
        string apiKey = "";

        if (result.IsSuccess)
        {
            connection.SetAuthenticated(result.User!);
            _logger.LogInformation("User {Username} authenticated from {Connection}",
                result.User!.Username, connection.RemoteEndPoint);

            apiKey = (await _apiKeyRepository.CreateAsync(result.User.Id, "Default Key")).Key;
        }

        var response = new AuthResponse
        {
            Success = result.IsSuccess,
            Message = result.Error ?? "Authentication successful",
            UserId = result.User?.Id.ToString() ?? "",
            IsAdmin = result.User?.IsAdmin ?? false,
            ApiKey = apiKey
        };

        await connection.SendPacketAsync(PacketType.AuthResponse, response, cancellationToken);
    }

    private async Task HandleAuthRegisterAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var request = AuthRegister.Parser.ParseFrom(payload);

        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
        {
            await SendAuthResponseAsync(connection, false, "Username must be at least 3 characters", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            await SendAuthResponseAsync(connection, false, "Password must be at least 8 characters", cancellationToken);
            return;
        }

        var result = await _authService.RegisterUserAsync(request.Username, request.Password, connection.DeviceName);

        if (result == null)
        {
            await SendAuthResponseAsync(connection, false, "Username already exists", cancellationToken);
            return;
        }

        connection.SetAuthenticated(result.Value.User);
        _logger.LogInformation("New user {Username} registered from {Connection}",
            result.Value.User.Username, connection.RemoteEndPoint);

        var response = new AuthResponse
        {
            Success = true,
            Message = "Registration successful",
            UserId = result.Value.User.Id.ToString(),
            ApiKey = result.Value.ApiKey,
            IsAdmin = result.Value.User.IsAdmin
        };

        await connection.SendPacketAsync(PacketType.AuthResponse, response, cancellationToken);
    }

    private async Task HandleAuthApiKeyAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var request = AuthApiKey.Parser.ParseFrom(payload);
        var result = await _authService.AuthenticateWithApiKeyAsync(request.ApiKey);

        if (result.IsSuccess)
        {
            connection.SetAuthenticated(result.User!);
            _logger.LogInformation("User {Username} authenticated via API key from {Connection}",
                result.User!.Username, connection.RemoteEndPoint);
        }

        var response = new AuthResponse
        {
            Success = result.IsSuccess,
            Message = result.Error ?? "Authentication successful",
            UserId = result.User?.Id.ToString() ?? "",
            IsAdmin = result.User?.IsAdmin ?? false
        };

        await connection.SendPacketAsync(PacketType.AuthResponse, response, cancellationToken);
    }

    private Task HandleAuthLogoutAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        if (connection.IsAuthenticated)
        {
            _logger.LogInformation("User {Username} logged out from {Connection}",
                connection.AuthenticatedUser!.Username, connection.RemoteEndPoint);
        }

        connection.Disconnect();
        return Task.CompletedTask;
    }

    private async Task HandleClipboardPushAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!RequireAuth(connection, cancellationToken, out var userId))
            return;

        var request = ClipboardPush.Parser.ParseFrom(payload);
        var entry = request.Entry;

        var dbEntry = await _clipboardService.PushEntryAsync(
            userId,
            entry.ContentType,
            entry.Content.ToByteArray(),
            entry.ContentPreview,
            entry.SourceDevice,
            connection.Id,
            cancellationToken);

        var response = new ClipboardPushAck
        {
            Success = true,
            EntryId = dbEntry.Id.ToString(),
            Message = "Entry saved"
        };

        await connection.SendPacketAsync(PacketType.ClipboardPushAck, response, cancellationToken);
    }

    private async Task HandleClipboardPullAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!RequireAuth(connection, cancellationToken, out var userId))
            return;

        var request = ClipboardPull.Parser.ParseFrom(payload);
        var limit = request.Limit > 0 ? request.Limit : 100;
        var offset = request.Offset > 0 ? request.Offset : 0;

        var entries = await _clipboardService.GetHistoryAsync(userId, limit, offset);
        var totalCount = await _clipboardService.GetHistoryCountAsync(userId);

        var response = new ClipboardHistory();
        foreach (var entry in entries)
        {
            response.Entries.Add(_clipboardService.ToProtoEntry(entry));
        }
        response.TotalCount = totalCount;
        response.HasMore = (offset + response.Entries.Count) < totalCount;

        await connection.SendPacketAsync(PacketType.ClipboardHistory, response, cancellationToken);
    }

    private async Task HandleClipboardSearchAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!RequireAuth(connection, cancellationToken, out var userId))
            return;

        var request = ClipboardSearch.Parser.ParseFrom(payload);
        var limit = request.Limit > 0 ? request.Limit : 50;

        var entries = await _clipboardService.SearchAsync(userId, request.Query, limit);

        var response = new ClipboardSearchResult();
        foreach (var entry in entries)
        {
            response.Entries.Add(_clipboardService.ToProtoEntry(entry));
        }
        response.TotalMatches = response.Entries.Count;
        response.HasMore = false;

        await connection.SendPacketAsync(PacketType.ClipboardSearchResult, response, cancellationToken);
    }

    private async Task HandleClipboardMoveToTopAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!RequireAuth(connection, cancellationToken, out var userId))
            return;

        var request = ClipboardMoveToTop.Parser.ParseFrom(payload);

        if (!Guid.TryParse(request.EntryId, out var entryId))
        {
            await SendErrorAsync(connection, "Invalid entry ID", cancellationToken);
            return;
        }

        var success = await _clipboardService.MoveToTopAsync(userId, entryId);

        var response = new ClipboardMoveToTopAck
        {
            Success = success,
            Message = success ? "Entry moved to top" : "Entry not found"
        };

        await connection.SendPacketAsync(PacketType.ClipboardMoveToTopAck, response, cancellationToken);
    }

    private async Task HandleClipboardDeleteAsync(
        ClientConnection connection,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!RequireAuth(connection, cancellationToken, out var userId))
            return;

        var request = ClipboardDelete.Parser.ParseFrom(payload);

        if (!Guid.TryParse(request.EntryId, out var entryId))
        {
            await SendErrorAsync(connection, "Invalid entry ID", cancellationToken);
            return;
        }

        var success = await _clipboardService.DeleteEntryAsync(userId, entryId);

        var response = new ClipboardDeleteAck
        {
            Success = success,
            Message = success ? "Entry deleted" : "Entry not found"
        };

        await connection.SendPacketAsync(PacketType.ClipboardDeleteAck, response, cancellationToken);
    }

    private bool RequireAuth(ClientConnection connection, CancellationToken cancellationToken, out Guid userId)
    {
        if (!connection.IsAuthenticated)
        {
            _ = SendErrorAsync(connection, "Authentication required", cancellationToken);
            userId = Guid.Empty;
            return false;
        }

        userId = connection.AuthenticatedUser!.Id;
        return true;
    }

    private async Task SendAuthResponseAsync(
        ClientConnection connection,
        bool success,
        string message,
        CancellationToken cancellationToken)
    {
        var response = new AuthResponse
        {
            Success = success,
            Message = message
        };
        await connection.SendPacketAsync(PacketType.AuthResponse, response, cancellationToken);
    }

    private async Task SendErrorAsync(
        ClientConnection connection,
        string message,
        CancellationToken cancellationToken)
    {
        var error = new ErrorResponse { Message = message };
        await connection.SendPacketAsync(PacketType.Error, error, cancellationToken);
    }
}
