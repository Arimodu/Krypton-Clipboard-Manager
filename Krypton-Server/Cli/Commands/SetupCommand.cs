using System.CommandLine;
using System.CommandLine.Invocation;
using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;

namespace Krypton.Server.Cli.Commands;

public static class SetupCommand
{
    public static Command Create()
    {
        var command = new Command("setup", "Run initial server setup wizard");

        command.SetHandler(async (InvocationContext context) =>
        {
            await RunSetupAsync();
        });

        return command;
    }

    private static async Task<int> RunSetupAsync()
    {
        Console.WriteLine("=== Krypton Server Setup ===\n");

        // 1. Config file location
        var configPath = ConfigurationLoader.GetDefaultConfigPath();
        Console.Write($"Config file path [{configPath}]: ");
        var inputPath = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            configPath = inputPath;
        }

        // Check if config already exists
        if (File.Exists(configPath))
        {
            Console.Write("Config file already exists. Overwrite? [y/N]: ");
            var overwrite = Console.ReadLine();
            if (!overwrite?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine("Keeping existing config.");
            }
            else
            {
                CreateConfig(configPath);
            }
        }
        else
        {
            CreateConfig(configPath);
        }

        // Load config
        var config = ConfigurationLoader.Load(configPath);

        // 2. Database setup
        Console.WriteLine("\n--- Database Setup ---");
        Console.Write($"Database provider (sqlite/postgresql) [{config.Database.Provider}]: ");
        var provider = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            config.Database.Provider = provider;
        }

        Console.Write($"Connection string [{config.Database.ConnectionString}]: ");
        var connStr = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            config.Database.ConnectionString = connStr;
        }

        Console.Write("Encrypt database at rest? [y/N]: ");
        var encrypt = Console.ReadLine();
        config.Database.Encrypt = encrypt?.Equals("y", StringComparison.OrdinalIgnoreCase) == true;

        // Initialize database
        Console.WriteLine("\nInitializing database...");
        DatabaseFactory.EnsureDirectoryExists(config.Database.ConnectionString);
        var dbOptions = DatabaseFactory.CreateOptions(config.Database);
        await using var context = new KryptonDbContext(dbOptions);
        await DatabaseFactory.EnsureDatabaseCreatedAsync(context);
        Console.WriteLine("Database initialized.");

        // 3. Create admin user
        Console.WriteLine("\n--- Admin User Setup ---");
        var userRepo = new UserRepository(context);

        string username;
        while (true)
        {
            Console.Write("Admin username: ");
            username = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Username cannot be empty.");
                continue;
            }
            if (await userRepo.ExistsAsync(username))
            {
                Console.WriteLine("Username already exists. Choose another.");
                continue;
            }
            break;
        }

        string password;
        while (true)
        {
            Console.Write("Admin password: ");
            password = ReadPassword();
            Console.WriteLine();
            if (password.Length < 8)
            {
                Console.WriteLine("Password must be at least 8 characters.");
                continue;
            }
            Console.Write("Confirm password: ");
            var confirm = ReadPassword();
            Console.WriteLine();
            if (password != confirm)
            {
                Console.WriteLine("Passwords do not match.");
                continue;
            }
            break;
        }

        var adminUser = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = true
        };
        await userRepo.CreateAsync(adminUser);
        Console.WriteLine($"Admin user '{username}' created.");

        // 4. TLS setup
        Console.WriteLine("\n--- TLS Setup ---");
        Console.Write("Enable TLS? [Y/n]: ");
        var enableTls = Console.ReadLine();
        config.Tls.Mode = enableTls?.Equals("n", StringComparison.OrdinalIgnoreCase) == true
            ? TlsMode.Off
            : TlsMode.Enabled;

        if (config.Tls.Mode != TlsMode.Off)
        {
            Console.Write("Domain for LetsEncrypt: ");
            config.Tls.Domain = Console.ReadLine() ?? "";

            Console.Write("Email for LetsEncrypt: ");
            config.Tls.LetsEncrypt.Email = Console.ReadLine() ?? "";

            Console.Write("Use staging (testing) server? [y/N]: ");
            var staging = Console.ReadLine();
            config.Tls.LetsEncrypt.Staging = staging?.Equals("y", StringComparison.OrdinalIgnoreCase) == true;
        }

        // Save updated config
        SaveConfig(configPath, config);

        Console.WriteLine("\n=== Setup Complete ===");
        Console.WriteLine($"Config saved to: {configPath}");
        Console.WriteLine("Start the server with: krypton-server start");

        return 0;
    }

    private static void CreateConfig(string path)
    {
        ConfigurationLoader.SaveDefaultConfig(path);
        Console.WriteLine($"Created default config at: {path}");
    }

    private static void SaveConfig(string path, ServerConfiguration config)
    {
        var toml = $"""
            # Krypton Server Configuration

            [server]
            port = {config.Server.Port}
            bind_address = "{config.Server.BindAddress}"
            max_connections = {config.Server.MaxConnections}
            heartbeat_interval_ms = {config.Server.HeartbeatIntervalMs}
            connection_timeout_ms = {config.Server.ConnectionTimeoutMs}

            [database]
            provider = "{config.Database.Provider}"
            connection_string = "{config.Database.ConnectionString}"
            encrypt = {config.Database.Encrypt.ToString().ToLower()}

            [cleanup]
            enabled = {config.Cleanup.Enabled.ToString().ToLower()}
            days = {config.Cleanup.Days}

            [tls]
            mode = "{config.Tls.Mode}"
            domain = "{config.Tls.Domain}"
            certificate_path = "{config.Tls.CertificatePath}"

            [tls.letsencrypt]
            enabled = {config.Tls.LetsEncrypt.Enabled.ToString().ToLower()}
            email = "{config.Tls.LetsEncrypt.Email}"
            staging = {config.Tls.LetsEncrypt.Staging.ToString().ToLower()}

            [logging]
            level = "{config.Logging.Level}"
            file_path = "{config.Logging.FilePath}"
            """;

        ConfigurationLoader.EnsureConfigDirectoryExists(path);
        File.WriteAllText(path, toml);
    }

    private static string ReadPassword()
    {
        var password = "";
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password += key.KeyChar;
                Console.Write("*");
            }
        }
        return password;
    }
}
