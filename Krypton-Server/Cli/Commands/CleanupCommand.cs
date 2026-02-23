using System.CommandLine;
using System.CommandLine.Invocation;
using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Repositories;

namespace Krypton.Server.Cli.Commands;

public static class CleanupCommand
{
    public static Command Create()
    {
        var command = new Command("cleanup", "Clean up old clipboard entries");

        var daysOption = new Option<int>(
            name: "--days",
            description: "Delete entries older than this many days",
            getDefaultValue: () => 30);
        daysOption.AddAlias("-d");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Show what would be deleted without actually deleting");

        command.AddOption(daysOption);
        command.AddOption(configOption);
        command.AddOption(dryRunOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var days = context.ParseResult.GetValueForOption(daysOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var repo = new ClipboardEntryRepository(dbContext);

            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            if (dryRun)
            {
                Console.WriteLine($"Would delete entries older than {days} days (before {cutoffDate:yyyy-MM-dd HH:mm})");
                Console.WriteLine("Use without --dry-run to actually delete.");
            }
            else
            {
                Console.WriteLine($"Deleting entries older than {days} days (before {cutoffDate:yyyy-MM-dd HH:mm})...");
                var deleted = await repo.CleanupOldEntriesAsync(days);
                Console.WriteLine($"Deleted {deleted} entries.");
            }
        });

        return command;
    }

    private static ServerConfiguration LoadConfig(string? configPath)
    {
        configPath ??= ConfigurationLoader.GetDefaultConfigPath();
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            Console.WriteLine("Run 'krypton-server setup' first.");
            Environment.Exit(1);
        }
        return ConfigurationLoader.Load(configPath);
    }

    private static KryptonDbContext CreateContext(ServerConfiguration config)
    {
        var options = DatabaseFactory.CreateOptions(config.Database);
        return new KryptonDbContext(options);
    }
}
