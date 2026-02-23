using System.Security.Cryptography.X509Certificates;
using Krypton.Server.Configuration;
using Microsoft.Extensions.Logging;

namespace Krypton.Server.Networking;

/// <summary>
/// Provides TLS certificates from file or LetsEncrypt.
/// </summary>
public class FileCertificateProvider : ICertificateProvider
{
    private readonly ServerConfiguration _config;
    private readonly ILogger<FileCertificateProvider> _logger;
    private X509Certificate2? _certificate;

    public FileCertificateProvider(ServerConfiguration config, ILogger<FileCertificateProvider> logger)
    {
        _config = config;
        _logger = logger;
        LoadCertificate();
    }

    public X509Certificate2? GetCertificate()
    {
        return _certificate;
    }

    public Task<bool> RenewCertificateAsync(CancellationToken cancellationToken)
    {
        // TODO: Implement LetsEncrypt renewal with Certes in a future phase
        _logger.LogWarning("Certificate renewal not yet implemented");
        return Task.FromResult(false);
    }

    private void LoadCertificate()
    {
        if (_config.Tls.Mode == TlsMode.Off)
        {
            _logger.LogInformation("TLS is disabled");
            return;
        }

        var certPath = _config.Tls.CertificatePath;
        if (string.IsNullOrEmpty(certPath))
        {
            _logger.LogWarning("TLS enabled but no certificate path configured");
            return;
        }

        if (!File.Exists(certPath))
        {
            _logger.LogWarning("Certificate file not found: {Path}", certPath);
            return;
        }

        try
        {
            _certificate = new X509Certificate2(certPath);
            _logger.LogInformation("Loaded certificate: {Subject}, expires: {Expiry}",
                _certificate.Subject, _certificate.NotAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificate from {Path}", certPath);
        }
    }
}
