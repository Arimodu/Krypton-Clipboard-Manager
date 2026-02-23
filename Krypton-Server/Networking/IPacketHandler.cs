using Krypton.Shared.Protocol;

namespace Krypton.Server.Networking;

/// <summary>
/// Interface for handling incoming packets from clients.
/// </summary>
public interface IPacketHandler
{
    /// <summary>
    /// Handles a client connection.
    /// </summary>
    /// <param name="connection">The client connection.</param>
    /// <param name="initialPacket">Optional initial packet received during handshake (e.g., Connect if TLS was skipped).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleConnectionAsync(
        ClientConnection connection,
        (PacketType Type, byte[] Payload)? initialPacket,
        CancellationToken cancellationToken);
}
