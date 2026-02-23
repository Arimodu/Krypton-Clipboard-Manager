using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Krypton.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Krypton.Server.Networking;

/// <summary>
/// TCP server that accepts and manages client connections.
/// </summary>
public class TcpServer : BackgroundService
{
    private readonly ServerConfiguration _config;
    private readonly ConnectionManager _connectionManager;
    private readonly IPacketHandler _packetHandler;
    private readonly ILogger<TcpServer> _logger;
    private readonly SslServerAuthenticationOptions? _sslOptions;
    private TcpListener? _listener;

    public TcpServer(
        ServerConfiguration config,
        ConnectionManager connectionManager,
        IPacketHandler packetHandler,
        ICertificateProvider? certificateProvider,
        ILogger<TcpServer> logger)
    {
        _config = config;
        _connectionManager = connectionManager;
        _packetHandler = packetHandler;
        _logger = logger;

        if (_config.Tls.Mode != TlsMode.Off && certificateProvider != null)
        {
            var cert = certificateProvider.GetCertificate();
            if (cert != null)
            {
                _sslOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                          System.Security.Authentication.SslProtocols.Tls13
                };
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(
            IPAddress.Parse(_config.Server.BindAddress),
            _config.Server.Port);

        _listener = new TcpListener(endpoint);
        _listener.Start();

        _logger.LogInformation("TCP server listening on {Endpoint} (TLS: {TlsEnabled})",
            endpoint, _sslOptions != null);

        // Start heartbeat monitor
        _ = MonitorConnectionsAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(stoppingToken);

                    if (_connectionManager.ConnectionCount >= _config.Server.MaxConnections)
                    {
                        _logger.LogWarning("Max connections reached, rejecting connection from {Remote}",
                            tcpClient.Client.RemoteEndPoint);
                        tcpClient.Dispose();
                        continue;
                    }

                    // Handle connection in background
                    _ = HandleConnectionAsync(tcpClient, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting connection");
                }
            }
        }
        finally
        {
            _listener.Stop();
            await _connectionManager.DisconnectAllAsync();
            _logger.LogInformation("TCP server stopped");
        }
    }

    private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken stoppingToken)
    {
        ClientConnection? connection = null;

        try
        {
            var (conn, initialPacket) = await ClientConnection.CreateWithHandshakeAsync(
                tcpClient, _sslOptions, _config.Tls.Mode, stoppingToken);
            connection = conn;
            _connectionManager.Add(connection);

            _logger.LogDebug("Client connected: {Id} from {Remote} (TLS: {TlsEnabled})",
                connection.Id, connection.RemoteEndPoint, connection.IsTlsEnabled);

            await _packetHandler.HandleConnectionAsync(connection, initialPacket, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Connection error for {Remote}",
                tcpClient.Client.RemoteEndPoint);
        }
        finally
        {
            if (connection != null)
            {
                _connectionManager.Remove(connection);
                await connection.DisposeAsync();

                _logger.LogDebug("Client disconnected: {Id} ({User})",
                    connection.Id, connection.AuthenticatedUser?.Username ?? "unauthenticated");
            }
            else
            {
                tcpClient.Dispose();
            }
        }
    }

    private async Task MonitorConnectionsAsync(CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromMilliseconds(_config.Server.ConnectionTimeoutMs);
        var checkInterval = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, stoppingToken);

                var staleConnections = _connectionManager.GetStaleConnections(timeout).ToList();
                foreach (var connection in staleConnections)
                {
                    _logger.LogDebug("Disconnecting stale connection: {Id}", connection.Id);
                    connection.Disconnect();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
