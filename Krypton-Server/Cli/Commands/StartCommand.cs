using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Repositories;
using Krypton.Server.Networking;
using Krypton.Server.Services;
using Krypton.Shared.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Krypton.Server.Cli.Commands;

public static class StartCommand
{
    public static Command Create()
    {
        var command = new Command("start", "Start the Krypton server");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging to console (all log levels)");
        verboseOption.AddAlias("-v");

        var debugOption = new Option<bool>(
            name: "--debug",
            description: "Enable debug logging to console");
        verboseOption.AddAlias("-d");

        command.AddOption(configOption);
        command.AddOption(verboseOption);
        command.AddOption(debugOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var debug = context.ParseResult.GetValueForOption(debugOption);
            var cancellationToken = context.GetCancellationToken();
            var consoleLogLevel = LogEventLevel.Fatal;
            if (debug) consoleLogLevel = LogEventLevel.Debug;
            if (verbose) consoleLogLevel = LogEventLevel.Verbose;
            await StartServerAsync(configPath, consoleLogLevel, cancellationToken);
        });

        return command;
    }

    private static void PrintSplashScreen()
    {
        const int width = 54;
        var version = PacketConstants.FullVersion;
        var contributors = string.Join(", ", BuildContributors.Top5);
        const string desc = "Cross-platform clipboard sync";

        static string Pad(string label, string value, int totalWidth)
        {
            var content = $"  {label,-14} {value}";
            var padding = totalWidth - content.Length - 1;
            return $"║{content}{new string(' ', Math.Max(0, padding))}║";
        }

        Console.WriteLine($"╔{new string('═', width)}╗");
        Console.WriteLine($"║{"KRYPTON CLIPBOARD SERVER".PadLeft((width + "KRYPTON CLIPBOARD SERVER".Length) / 2).PadRight(width)}║");
        Console.WriteLine($"╠{new string('═', width)}╣");
        Console.WriteLine(Pad("Version:", version, width + 2));
        Console.WriteLine(Pad("Description:", desc, width + 2));
        Console.WriteLine($"╠{new string('═', width)}╣");
        Console.WriteLine(Pad("Contributors:", contributors, width + 2));
        Console.WriteLine($"╚{new string('═', width)}╝");
        Console.WriteLine();
    }

    private static async Task StartServerAsync(string? configPath, LogEventLevel consoleLevelOverride, CancellationToken cancellationToken)
    {
        PrintSplashScreen();
        configPath ??= ConfigurationLoader.GetDefaultConfigPath();

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            Console.WriteLine("Run 'krypton-server setup' to create initial configuration.");
            return;
        }

        var config = ConfigurationLoader.Load(configPath);

        // Configure Serilog

        var configLevel = ParseLogLevel(config.Logging.Level);

        var consoleLevel = consoleLevelOverride < configLevel ? consoleLevelOverride : configLevel;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(restrictedToMinimumLevel: consoleLevel)
            .WriteTo.File(
                config.Logging.FilePath,
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            var host = Host.CreateDefaultBuilder()
                .UseSerilog(Log.Logger)
                .ConfigureServices(services =>
                {
                    // Configuration
                    services.AddSingleton(config);

                    // Database
                    services.AddDbContext<KryptonDbContext>(options =>
                    {
                        if (config.Database.Provider.Equals("postgresql", StringComparison.InvariantCultureIgnoreCase))
                        {
                            options.UseNpgsql(config.Database.ConnectionString);
                        }
                        else
                        {
                            // Convert path to proper SQLite connection string if needed
                            var connectionString = config.Database.ConnectionString;
                            if (!connectionString.Contains('='))
                            {
                                connectionString = $"Data Source={connectionString}";
                            }

                            // Ensure directory exists
                            DatabaseFactory.EnsureDirectoryExists(connectionString);

                            options.UseSqlite(connectionString);
                        }
                    });

                    // Repositories
                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
                    services.AddScoped<IClipboardEntryRepository, ClipboardEntryRepository>();

                    // Services
                    services.AddScoped<AuthenticationService>();
                    services.AddScoped<ClipboardService>();

                    // Networking
                    services.AddSingleton<ConnectionManager>();
                    services.AddSingleton<ICertificateProvider, FileCertificateProvider>();
                    services.AddSingleton<IPacketHandler, PacketHandler>();
                    services.AddHostedService<TcpServer>();

                    // Background services
                    if (config.Cleanup.Enabled)
                    {
                        services.AddHostedService<CleanupService>();
                    }
                })
                .Build();

            // Ensure database is created, then apply any schema upgrades for existing DBs
            using (var scope = host.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<KryptonDbContext>();
                await context.Database.EnsureCreatedAsync(cancellationToken);
                await DatabaseMigrator.ApplySchemaUpgradesAsync(context);
            }

            Console.WriteLine($"Starting Krypton Server v{PacketConstants.FullVersion}...");
            Console.WriteLine($"  Port: {config.Server.Port}");
            Console.WriteLine($"  Database: {config.Database.Provider}");
            Console.WriteLine($"  TLS: {config.Tls.Mode}");
            Console.WriteLine($"  Cleanup: {(config.Cleanup.Enabled ? $"Enabled ({config.Cleanup.Days} days)" : "Disabled")}");

            await host.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nServer stopped.");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static Serilog.Events.LogEventLevel ParseLogLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "verbose" or "trace" => Serilog.Events.LogEventLevel.Verbose,
            "debug" => Serilog.Events.LogEventLevel.Debug,
            "info" or "information" => Serilog.Events.LogEventLevel.Information,
            "warn" or "warning" => Serilog.Events.LogEventLevel.Warning,
            "error" => Serilog.Events.LogEventLevel.Error,
            "fatal" => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Information
        };
    }
}
