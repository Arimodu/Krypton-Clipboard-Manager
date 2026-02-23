using Krypton.Server.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Krypton.Server.Database;

public static class DatabaseFactory
{
    public static DbContextOptions<KryptonDbContext> CreateOptions(DatabaseSection config)
    {
        var optionsBuilder = new DbContextOptionsBuilder<KryptonDbContext>();

        switch (config.Provider.ToLowerInvariant())
        {
            case "sqlite":
                ConfigureSqlite(optionsBuilder, config);
                break;

            case "postgresql":
            case "postgres":
                ConfigurePostgres(optionsBuilder, config);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported database provider: {config.Provider}. Supported: sqlite, postgresql");
        }

        return optionsBuilder.Options;
    }

    private static void ConfigureSqlite(DbContextOptionsBuilder<KryptonDbContext> builder, DatabaseSection config)
    {
        var connectionString = config.ConnectionString;

        // If it's just a path (not a connection string), convert it
        if (!connectionString.Contains('='))
        {
            connectionString = $"Data Source={connectionString}";
        }

        builder.UseSqlite(connectionString);
    }

    private static void ConfigurePostgres(DbContextOptionsBuilder<KryptonDbContext> builder, DatabaseSection config)
    {
        builder.UseNpgsql(config.ConnectionString);
    }

    public static async Task EnsureDatabaseCreatedAsync(KryptonDbContext context)
    {
        await context.Database.EnsureCreatedAsync();
    }

    public static async Task MigrateDatabaseAsync(KryptonDbContext context)
    {
        await context.Database.MigrateAsync();
    }

    public static void EnsureDirectoryExists(string connectionString)
    {
        // For SQLite, ensure the directory exists
        if (connectionString.Contains("Data Source="))
        {
            var path = connectionString
                .Split(';')
                .FirstOrDefault(p => p.Trim().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                ?.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (!string.IsNullOrEmpty(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }
        else if (!connectionString.Contains('='))
        {
            // It's just a path
            var directory = Path.GetDirectoryName(connectionString);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
