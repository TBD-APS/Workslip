using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Workslip.Api.Configuration;

internal static partial class LocalDevelopmentDatabaseMigrationRunner
{
    [GeneratedRegex(
        @"CREATE\s+TABLE\s+(?:\[?dbo\]?\s*\.\s*)?\[?(?<name>[A-Za-z_][A-Za-z0-9_]*)\]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTablePattern();

    // Names of the tables a migration would CREATE. Used to decide, on a fresh local
    // database, which migrations still have to run: EnsureCreated only builds tables that
    // are part of the EF model, so migration-managed (Dapper-only) tables are otherwise
    // missing after the schema baseline records every migration as already applied.
    internal static IReadOnlyList<string> CreatedTableNames(string sql) =>
        CreateTablePattern()
            .Matches(sql)
            .Select(match => match.Groups["name"].Value)
            .ToArray();

    // True when the migration introduces at least one table the EF model does not already
    // own. Such a migration must actually execute on a fresh database instead of only being
    // baselined, or the table would never exist locally.
    internal static bool CreatesTableMissingFromSchema(
        string sql,
        IReadOnlySet<string> existingTables)
    {
        var createdTables = CreatedTableNames(sql);
        return createdTables.Count > 0
            && createdTables.Any(table => !existingTables.Contains(table));
    }

    // Fresh-database completion step. EnsureCreated builds the EF-modeled schema and the
    // baseline records all versioned migrations as applied. Migration-managed tables that
    // are not part of the EF model (for example the Dapper-owned billing and knowledge-
    // document tables) would otherwise be recorded as applied without ever being created.
    // Run exactly the migrations that create those missing tables so a fresh local schema
    // matches what an incrementally migrated database has.
    public static async Task ApplyMissingTableMigrationsAsync(
        string connectionString,
        string contentRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalSqlTarget(connectionString))
        {
            throw new InvalidOperationException(
                "Local fresh-schema completion refused the configured SQL target because it is not provably local.");
        }

        var migrationsPath = Path.GetFullPath(Path.Combine(
            contentRootPath,
            "..",
            "infrastructure",
            "database",
            "migrations"));
        var migrations = LoadMigrations(migrationsPath);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureMigrationHistoryTableAsync(connection, cancellationToken);

        var existingTables = await GetExistingDboTableNamesAsync(connection, cancellationToken);

        var appliedCount = 0;
        foreach (var migration in migrations)
        {
            if (!CreatesTableMissingFromSchema(migration.Sql, existingTables))
                continue;

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireMigrationLockAsync(connection, transaction, cancellationToken);
                var storedChecksum = await GetStoredChecksumAsync(
                    connection,
                    transaction,
                    migration.Id,
                    cancellationToken);

                if (storedChecksum is null)
                {
                    await ExecuteMigrationAsync(connection, transaction, migration, cancellationToken);
                    await RecordMigrationAsync(connection, transaction, migration, cancellationToken);
                    appliedCount++;
                    Log.Information(
                        "[STARTUP 08.1] Created migration-managed table(s) absent from the EF model: {MigrationId}",
                        migration.Id);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            foreach (var createdTable in CreatedTableNames(migration.Sql))
                existingTables.Add(createdTable);
        }

        Log.Information(
            "[STARTUP 08.1] Local fresh-schema completion applied {AppliedCount} migration-managed table migration(s).",
            appliedCount);
    }

    private static async Task<HashSet<string>> GetExistingDboTableNamesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT tableInfo.name
            FROM sys.tables AS tableInfo
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.schema_id = tableInfo.schema_id
            WHERE schemaInfo.name = N'dbo'
              AND tableInfo.is_ms_shipped = 0;
            """;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            names.Add(reader.GetString(0));

        return names;
    }
}
