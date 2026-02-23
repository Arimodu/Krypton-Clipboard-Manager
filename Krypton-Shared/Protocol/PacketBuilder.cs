namespace Krypton.Shared.Protocol;

/// <summary>
/// Helper class for building KryptonPacket instances.
/// </summary>
public static class PacketBuilder
{
    private static uint _sequenceId;

    private static uint NextSequenceId() => Interlocked.Increment(ref _sequenceId);

    public static KryptonPacket Heartbeat() => new()
    {
        Type = PacketType.Heartbeat,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        Heartbeat = new Heartbeat()
    };

    public static KryptonPacket HeartbeatAck() => new()
    {
        Type = PacketType.HeartbeatAck,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        HeartbeatAck = new HeartbeatAck()
    };

    public static KryptonPacket Connect(string clientVersion, string platform, string deviceId) => new()
    {
        Type = PacketType.Connect,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        Connect = new Connect
        {
            ClientVersion = clientVersion,
            Platform = platform,
            DeviceId = deviceId
        }
    };

    public static KryptonPacket ConnectAck(string serverVersion, bool requiresAuth) => new()
    {
        Type = PacketType.ConnectAck,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ConnectAck = new ConnectAck
        {
            ServerVersion = serverVersion,
            RequiresAuth = requiresAuth
        }
    };

    public static KryptonPacket Disconnect(string reason = "") => new()
    {
        Type = PacketType.Disconnect,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        Disconnect = new Disconnect { Reason = reason }
    };

    public static KryptonPacket AuthLogin(string username, string password) => new()
    {
        Type = PacketType.AuthLogin,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        AuthLogin = new AuthLogin
        {
            Username = username,
            Password = password
        }
    };

    public static KryptonPacket AuthRegister(string username, string password) => new()
    {
        Type = PacketType.AuthRegister,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        AuthRegister = new AuthRegister
        {
            Username = username,
            Password = password
        }
    };

    public static KryptonPacket AuthApiKey(string apiKey) => new()
    {
        Type = PacketType.AuthApiKey,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        AuthApiKey = new AuthApiKey { ApiKey = apiKey }
    };

    public static KryptonPacket AuthResponse(bool success, string message, string apiKey = "", string userId = "", bool isAdmin = false) => new()
    {
        Type = PacketType.AuthResponse,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        AuthResponse = new AuthResponse
        {
            Success = success,
            Message = message,
            ApiKey = apiKey,
            UserId = userId,
            IsAdmin = isAdmin
        }
    };

    public static KryptonPacket AuthLogout() => new()
    {
        Type = PacketType.AuthLogout,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        AuthLogout = new AuthLogout()
    };

    public static KryptonPacket ClipboardPush(ClipboardEntry entry) => new()
    {
        Type = PacketType.ClipboardPush,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardPush = new ClipboardPush { Entry = entry }
    };

    public static KryptonPacket ClipboardPushAck(bool success, string entryId, string message = "") => new()
    {
        Type = PacketType.ClipboardPushAck,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardPushAck = new ClipboardPushAck
        {
            Success = success,
            EntryId = entryId,
            Message = message
        }
    };

    public static KryptonPacket ClipboardPull(int limit = 100, ulong sinceTimestamp = 0) => new()
    {
        Type = PacketType.ClipboardPull,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardPull = new ClipboardPull
        {
            Limit = limit,
            SinceTimestamp = sinceTimestamp
        }
    };

    public static KryptonPacket ClipboardHistory(IEnumerable<ClipboardEntry> entries, int totalCount, bool hasMore = false) => new()
    {
        Type = PacketType.ClipboardHistory,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardHistory = new ClipboardHistory
        {
            Entries = { entries },
            TotalCount = totalCount,
            HasMore = hasMore
        }
    };

    public static KryptonPacket ClipboardBroadcast(ClipboardEntry entry, string fromDevice) => new()
    {
        Type = PacketType.ClipboardBroadcast,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardBroadcast = new ClipboardBroadcast
        {
            Entry = entry,
            FromDevice = fromDevice
        }
    };

    public static KryptonPacket ClipboardSearch(string query, int limit = 50, ulong beforeTimestamp = 0) => new()
    {
        Type = PacketType.ClipboardSearch,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardSearch = new ClipboardSearch
        {
            Query = query,
            Limit = limit,
            BeforeTimestamp = beforeTimestamp
        }
    };

    public static KryptonPacket ClipboardSearchResult(IEnumerable<ClipboardEntry> entries, int totalMatches, bool hasMore = false) => new()
    {
        Type = PacketType.ClipboardSearchResult,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardSearchResult = new ClipboardSearchResult
        {
            Entries = { entries },
            TotalMatches = totalMatches,
            HasMore = hasMore
        }
    };

    public static KryptonPacket ClipboardMoveToTop(string entryId) => new()
    {
        Type = PacketType.ClipboardMoveToTop,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardMoveToTop = new ClipboardMoveToTop { EntryId = entryId }
    };

    public static KryptonPacket ClipboardMoveToTopAck(bool success, string message = "") => new()
    {
        Type = PacketType.ClipboardMoveToTopAck,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardMoveToTopAck = new ClipboardMoveToTopAck
        {
            Success = success,
            Message = message
        }
    };

    public static KryptonPacket ClipboardDelete(string entryId) => new()
    {
        Type = PacketType.ClipboardDelete,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardDelete = new ClipboardDelete { EntryId = entryId }
    };

    public static KryptonPacket ClipboardDeleteAck(bool success, string message = "") => new()
    {
        Type = PacketType.ClipboardDeleteAck,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        ClipboardDeleteAck = new ClipboardDeleteAck
        {
            Success = success,
            Message = message
        }
    };

    public static KryptonPacket Error(int code, string message) => new()
    {
        Type = PacketType.Error,
        Timestamp = PacketSerializer.GetTimestamp(),
        SequenceId = NextSequenceId(),
        Error = new ErrorResponse
        {
            Code = code,
            Message = message
        }
    };
}
