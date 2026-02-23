namespace Krypton.Server.Configuration;

public class ServerConfiguration
{
    public ServerSection Server { get; set; } = new();
    public DatabaseSection Database { get; set; } = new();
    public CleanupSection Cleanup { get; set; } = new();
    public TlsSection Tls { get; set; } = new();
    public LoggingSection Logging { get; set; } = new();
}

public class ServerSection
{
    public int Port { get; set; } = 6789;
    public string BindAddress { get; set; } = "0.0.0.0";
    public int MaxConnections { get; set; } = 1000;
    public int HeartbeatIntervalMs { get; set; } = 30000;
    public int ConnectionTimeoutMs { get; set; } = 120000;
}

public class DatabaseSection
{
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "/var/lib/krypton/krypton.db";
    public bool Encrypt { get; set; } = false;
    public string? EncryptionKey { get; set; }
}

public class CleanupSection
{
    public bool Enabled { get; set; } = false;
    public int Days { get; set; } = 30;
    public int IntervalHours { get; set; } = 1;  // How often to check for old entries
}

public enum TlsMode
{
    Off,
    Enabled,
    Required
}

public class TlsSection
{
    public TlsMode Mode { get; set; } = TlsMode.Enabled;
    public string Domain { get; set; } = "";
    public string CertificatePath { get; set; } = "/etc/krypton/certs/";
    public LetsEncryptSection LetsEncrypt { get; set; } = new();
}

public class LetsEncryptSection
{
    public bool Enabled { get; set; } = true;
    public string Email { get; set; } = "";
    public bool Staging { get; set; } = false;
}

public class LoggingSection
{
    public string Level { get; set; } = "Information";
    public string FilePath { get; set; } = "/var/log/krypton/server.log";
}
