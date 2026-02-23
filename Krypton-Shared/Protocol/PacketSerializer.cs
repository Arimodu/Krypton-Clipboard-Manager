using System.Buffers.Binary;
using Google.Protobuf;

namespace Krypton.Shared.Protocol;

public static class PacketSerializer
{
    /// <summary>
    /// Reads a complete packet asynchronously from a stream.
    /// Returns (PacketType, payload bytes) or null if stream ended.
    /// </summary>
    public static async Task<(PacketType Type, byte[] Payload)?> ReadPacketAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        // Read header (4 bytes length + 2 bytes type)
        var header = new byte[PacketConstants.HeaderSize];
        var headerRead = await ReadExactAsync(stream, header, cancellationToken);
        if (!headerRead)
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        var type = (PacketType)BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));

        // Read payload (length - 2 for type already read)
        var payloadLength = length - 2;
        if (payloadLength < 0)
            return null;

        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            var payloadRead = await ReadExactAsync(stream, payload, cancellationToken);
            if (!payloadRead)
                return null;
        }

        return (type, payload);
    }

    /// <summary>
    /// Writes a packet asynchronously to a stream.
    /// </summary>
    public static async Task WritePacketAsync(
        Stream stream,
        PacketType type,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var totalLength = payload.Length + 2; // +2 for type field
        var header = new byte[PacketConstants.HeaderSize];

        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), (ushort)type);

        await stream.WriteAsync(header, cancellationToken);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }

    /// <summary>
    /// Serializes a KryptonPacket to bytes with wire format header.
    /// Wire format: [4-byte length (big-endian)][2-byte type (big-endian)][protobuf payload]
    /// </summary>
    public static byte[] Serialize(KryptonPacket packet)
    {
        var payload = packet.ToByteArray();
        var totalLength = payload.Length + 2; // +2 for type field

        var buffer = new byte[4 + totalLength];

        // Write length (big-endian)
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), totalLength);

        // Write type (big-endian)
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), (ushort)packet.Type);

        // Write payload
        payload.CopyTo(buffer, 6);

        return buffer;
    }

    /// <summary>
    /// Reads the packet header to get length and type.
    /// Returns (totalLength, packetType) or null if not enough data.
    /// </summary>
    public static (int Length, PacketType Type)? ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < PacketConstants.HeaderSize)
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
        var type = (PacketType)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));

        return (length, type);
    }

    /// <summary>
    /// Deserializes bytes to a KryptonPacket.
    /// Expects data starting after the 4-byte length header.
    /// </summary>
    public static KryptonPacket Deserialize(ReadOnlySpan<byte> data)
    {
        // Skip the 2-byte type field (we already know it from header)
        var payload = data[2..];
        return KryptonPacket.Parser.ParseFrom(payload);
    }

    /// <summary>
    /// Tries to read a complete packet from a buffer.
    /// Returns the packet and consumed bytes count, or null if incomplete.
    /// </summary>
    public static (KryptonPacket Packet, int BytesConsumed)? TryReadPacket(ReadOnlySpan<byte> buffer)
    {
        var header = ReadHeader(buffer);
        if (header == null)
            return null;

        var totalPacketSize = 4 + header.Value.Length; // 4-byte length header + payload
        if (buffer.Length < totalPacketSize)
            return null;

        var packetData = buffer.Slice(4, header.Value.Length);
        var packet = Deserialize(packetData);

        return (packet, totalPacketSize);
    }

    /// <summary>
    /// Creates a timestamp for the current time in milliseconds since Unix epoch.
    /// </summary>
    public static ulong GetTimestamp()
    {
        return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Converts a protobuf timestamp to DateTimeOffset.
    /// </summary>
    public static DateTimeOffset FromTimestamp(ulong timestamp)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp);
    }
}
