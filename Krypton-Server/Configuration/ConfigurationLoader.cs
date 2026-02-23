using Tomlyn;
using Tomlyn.Model;

namespace Krypton.Server.Configuration;

public static class ConfigurationLoader
{
    private const string DefaultLinuxConfigPath = "/etc/krypton/config.toml";
    private const string DefaultWindowsConfigPath = "config.toml";

    public static string GetDefaultConfigPath()
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, DefaultWindowsConfigPath)
            : DefaultLinuxConfigPath;
    }

    public static ServerConfiguration Load(string? configPath = null)
    {
        configPath ??= GetDefaultConfigPath();

        if (!File.Exists(configPath))
        {
            return new ServerConfiguration();
        }

        var tomlContent = File.ReadAllText(configPath);
        return Parse(tomlContent);
    }

    public static ServerConfiguration Parse(string tomlContent)
    {
        var model = Toml.ToModel(tomlContent);
        var config = new ServerConfiguration();

        if (model.TryGetValue("server", out var serverObj) && serverObj is TomlTable serverTable)
        {
            config.Server = ParseServerSection(serverTable);
        }

        if (model.TryGetValue("database", out var dbObj) && dbObj is TomlTable dbTable)
        {
            config.Database = ParseDatabaseSection(dbTable);
        }

        if (model.TryGetValue("cleanup", out var cleanupObj) && cleanupObj is TomlTable cleanupTable)
        {
            config.Cleanup = ParseCleanupSection(cleanupTable);
        }

        if (model.TryGetValue("tls", out var tlsObj) && tlsObj is TomlTable tlsTable)
        {
            config.Tls = ParseTlsSection(tlsTable);
        }

        if (model.TryGetValue("logging", out var loggingObj) && loggingObj is TomlTable loggingTable)
        {
            config.Logging = ParseLoggingSection(loggingTable);
        }

        return config;
    }

    private static ServerSection ParseServerSection(TomlTable table)
    {
        var section = new ServerSection();

        if (table.TryGetValue("port", out var port))
            section.Port = Convert.ToInt32(port);

        if (table.TryGetValue("bind_address", out var bindAddress))
            section.BindAddress = bindAddress?.ToString() ?? section.BindAddress;

        if (table.TryGetValue("max_connections", out var maxConnections))
            section.MaxConnections = Convert.ToInt32(maxConnections);

        if (table.TryGetValue("heartbeat_interval_ms", out var heartbeat))
            section.HeartbeatIntervalMs = Convert.ToInt32(heartbeat);

        if (table.TryGetValue("connection_timeout_ms", out var timeout))
            section.ConnectionTimeoutMs = Convert.ToInt32(timeout);

        return section;
    }

    private static DatabaseSection ParseDatabaseSection(TomlTable table)
    {
        var section = new DatabaseSection();

        if (table.TryGetValue("provider", out var provider))
            section.Provider = provider?.ToString() ?? section.Provider;

        if (table.TryGetValue("connection_string", out var connStr))
            section.ConnectionString = connStr?.ToString() ?? section.ConnectionString;

        if (table.TryGetValue("encrypt", out var encrypt))
            section.Encrypt = Convert.ToBoolean(encrypt);

        if (table.TryGetValue("encryption_key", out var encKey))
            section.EncryptionKey = encKey?.ToString();

        return section;
    }

    private static CleanupSection ParseCleanupSection(TomlTable table)
    {
        var section = new CleanupSection();

        if (table.TryGetValue("enabled", out var enabled))
            section.Enabled = Convert.ToBoolean(enabled);

        if (table.TryGetValue("days", out var days))
            section.Days = Convert.ToInt32(days);

        return section;
    }

    private static TlsSection ParseTlsSection(TomlTable table)
    {
        var section = new TlsSection();

        if (table.TryGetValue("mode", out var mode))
        {
            section.Mode = Enum.TryParse<TlsMode>(mode?.ToString(), true, out var tlsMode)
                ? tlsMode
                : TlsMode.Enabled;
        }
        else if (table.TryGetValue("enabled", out var enabled))
        {
            // Backwards compatibility: convert old "enabled" bool to new Mode enum
            section.Mode = Convert.ToBoolean(enabled) ? TlsMode.Enabled : TlsMode.Off;
        }

        if (table.TryGetValue("domain", out var domain))
            section.Domain = domain?.ToString() ?? section.Domain;

        if (table.TryGetValue("certificate_path", out var certPath))
            section.CertificatePath = certPath?.ToString() ?? section.CertificatePath;

        if (table.TryGetValue("letsencrypt", out var leObj) && leObj is TomlTable leTable)
        {
            section.LetsEncrypt = ParseLetsEncryptSection(leTable);
        }

        return section;
    }

    private static LetsEncryptSection ParseLetsEncryptSection(TomlTable table)
    {
        var section = new LetsEncryptSection();

        if (table.TryGetValue("enabled", out var enabled))
            section.Enabled = Convert.ToBoolean(enabled);

        if (table.TryGetValue("email", out var email))
            section.Email = email?.ToString() ?? section.Email;

        if (table.TryGetValue("staging", out var staging))
            section.Staging = Convert.ToBoolean(staging);

        return section;
    }

    private static LoggingSection ParseLoggingSection(TomlTable table)
    {
        var section = new LoggingSection();

        if (table.TryGetValue("level", out var level))
            section.Level = level?.ToString() ?? section.Level;

        if (table.TryGetValue("file_path", out var filePath))
            section.FilePath = filePath?.ToString() ?? section.FilePath;

        return section;
    }

    public static void EnsureConfigDirectoryExists(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static void SaveDefaultConfig(string configPath)
    {
        EnsureConfigDirectoryExists(configPath);
        var defaultConfig = GenerateDefaultConfigToml();
        File.WriteAllText(configPath, defaultConfig);
    }

    public static string GenerateDefaultConfigToml()
    {
        return """
            # Krypton Server Configuration

            [server]
            port = 6789
            bind_address = "0.0.0.0"
            max_connections = 1000
            heartbeat_interval_ms = 30000
            connection_timeout_ms = 120000

            [database]
            provider = "sqlite"  # Options: "sqlite" or "postgresql"
            connection_string = "/var/lib/krypton/krypton.db"
            encrypt = false
            # encryption_key = ""  # Required if encrypt = true

            [cleanup]
            enabled = false  # Time-based auto-cleanup (off by default)
            days = 30        # Delete entries older than this many days

            [tls]
            enabled = true
            domain = ""  # Your domain for LetsEncrypt
            certificate_path = "/etc/krypton/certs/"

            [tls.letsencrypt]
            enabled = true
            email = ""  # Required for LetsEncrypt
            staging = false  # Set to true for testing

            [logging]
            level = "Information"  # Options: Verbose, Debug, Information, Warning, Error, Fatal
            file_path = "/var/log/krypton/server.log"
            """;
    }
}
