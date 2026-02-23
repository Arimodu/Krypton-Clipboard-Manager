using System.Security.Cryptography.X509Certificates;

namespace Krypton.Server.Networking;

/// <summary>
/// Interface for providing TLS certificates.
/// </summary>
public interface ICertificateProvider
{
    X509Certificate2? GetCertificate();
    Task<bool> RenewCertificateAsync(CancellationToken cancellationToken);
}
