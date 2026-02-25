using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Krypton.Server.Database;

/// <summary>
/// Incremental schema migration system. A <c>_SchemaVersion</c> table (single integer row)
/// tracks which migrations have been applied. On every startup, all migrations whose
/// version number exceeds the stored version are applied in order, then the stored version
/// is updated.
///
/// Adding a future migration:
///   1. Add a new entry to <see cref="Migrations"/> (must be exactly +1 from the previous).
///   2. Add a <c>case N:</c> to both <see cref="ApplySqliteMigrationAsync"/> and
///      <see cref="ApplyPostgresMigrationAsync"/> with the DDL for that version.
///
/// Each migration method should be idempotent (safe to run twice) as a defensive measure
/// against partial failures.
/// </summary>
public static class DatabaseMigrator
{
    private const string VersionTable = "_SchemaVersion";

    private record Migration(int Version, string Description);

    /// <summary>
    /// Ordered list of all migrations. Version numbers must be contiguous starting at 1.
    /// </summary>
    private static readonly Migration[] Migrations =
    [
        new(1, "Add ExternalStoragePath to ClipboardEntries"),
    ];

    // ── Entry point ───────────────────────────────────────────────────────────

    public static async Task ApplySchemaUpgradesAsync(KryptonDbContext context)
    {
        var provider = context.Database.ProviderName ?? string.Empty;
        var isSqlite   = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var isPostgres  = provider.Contains("Npgsql",  StringComparison.OrdinalIgnoreCase);

        if (!isSqlite && !isPostgres)
        {
            Log.Warning("DatabaseMigrator: unknown provider '{Provider}', skipping", provider);
            return;
        }

        var conn = context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();

        try
        {
            await EnsureVersionTableAsync(conn);
            var currentVersion = await GetVersionAsync(conn);
            var latestVersion  = Migrations[^1].Version;

            if (currentVersion >= latestVersion)
            {
                Log.Debug("Database schema is up to date (v{Version})", currentVersion);
                return;
            }

            foreach (var migration in Migrations.Where(m => m.Version > currentVersion))
            {
                Log.Information("Applying schema migration v{Version}: {Description}",
                    migration.Version, migration.Description);

                if (isSqlite)
                    await ApplySqliteMigrationAsync(conn, migration.Version);
                else
                    await ApplyPostgresMigrationAsync(conn, migration.Version);

                await SetVersionAsync(conn, migration.Version);
                Log.Information("Schema is now at v{Version}", migration.Version);
            }
        }
        finally
        {
            if (conn.State == ConnectionState.Open)
                await conn.CloseAsync();
        }
    }

    // ── Version table helpers ─────────────────────────────────────────────────

    private static async Task EnsureVersionTableAsync(DbConnection conn)
    {
        // Create the version table if it doesn't exist yet.
        await using var create = conn.CreateCommand();
        create.CommandText = $"""
            CREATE TABLE IF NOT EXISTS "{VersionTable}" ("Version" INTEGER NOT NULL DEFAULT 0);
            """;
        await create.ExecuteNonQueryAsync();

        // Ensure there is exactly one row (idempotent: only inserts when empty).
        await using var count = conn.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM \"{VersionTable}\"";
        var rows = Convert.ToInt64(await count.ExecuteScalarAsync() ?? 0L);

        if (rows == 0)
        {
            await using var seed = conn.CreateCommand();
            seed.CommandText = $"INSERT INTO \"{VersionTable}\" (\"Version\") VALUES (0)";
            await seed.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int> GetVersionAsync(DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"Version\" FROM \"{VersionTable}\" LIMIT 1";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private static async Task SetVersionAsync(DbConnection conn, int version)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE \"{VersionTable}\" SET \"Version\" = {version}";
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SQLite migrations ─────────────────────────────────────────────────────

    private static Task ApplySqliteMigrationAsync(DbConnection conn, int version) => version switch
    {
        1 => Sqlite_V1_AddExternalStoragePath(conn),
        _ => throw new InvalidOperationException($"No SQLite migration defined for v{version}")
    };

    /// <summary>v1 — Add nullable ExternalStoragePath column to ClipboardEntries.</summary>
    private static async Task Sqlite_V1_AddExternalStoragePath(DbConnection conn)
    {
        // Check first so the migration is idempotent.
        await using var check = conn.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('ClipboardEntries') WHERE name='ExternalStoragePath'";
        var exists = Convert.ToInt64(await check.ExecuteScalarAsync() ?? 0L) > 0;
        if (exists) return;

        await using var alter = conn.CreateCommand();
        alter.CommandText =
            "ALTER TABLE \"ClipboardEntries\" ADD COLUMN \"ExternalStoragePath\" TEXT NULL";
        await alter.ExecuteNonQueryAsync();
    }

    // ── PostgreSQL migrations ─────────────────────────────────────────────────

    private static Task ApplyPostgresMigrationAsync(DbConnection conn, int version) => version switch
    {
        1 => Postgres_V1_AddExternalStoragePath(conn),
        _ => throw new InvalidOperationException($"No PostgreSQL migration defined for v{version}")
    };

    /// <summary>v1 — Add nullable ExternalStoragePath column to ClipboardEntries.</summary>
    private static async Task Postgres_V1_AddExternalStoragePath(DbConnection conn)
    {
        // IF NOT EXISTS makes this idempotent on PostgreSQL.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "ALTER TABLE \"ClipboardEntries\" ADD COLUMN IF NOT EXISTS \"ExternalStoragePath\" VARCHAR(512) NULL";
        await cmd.ExecuteNonQueryAsync();
    }
}
