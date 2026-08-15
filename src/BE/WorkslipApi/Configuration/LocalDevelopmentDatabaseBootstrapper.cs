using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

internal static class LocalDevelopmentDatabaseBootstrapper
{
    private const string CoreTableName = "Organizations";
    private const string MigrationHistoryTableName = "WorkslipSchemaMigrations";

    public static async Task<bool> EnsureFreshSchemaAsync(
        SqlDbContext db,
        string connectionString,
        string contentRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(connectionString))
        {
            throw new InvalidOperationException(
                "Fresh local database bootstrap requires a provably local SQL target.");
        }

        // Handles both a database that does not exist yet and an existing database
        // with no tables at all.
        if (await db.Database.EnsureCreatedAsync(cancellationToken))
        {
            await CompleteFreshSchemaAsync(connectionString, contentRootPath, cancellationToken);
            Log.Information("[STARTUP 08.1] Fresh local database schema created from the current EF model");
            return true;
        }

        var dboTables = await GetDboTableNamesAsync(db, cancellationToken);
        var schemaState = ClassifySchemaState(dboTables);

        if (schemaState == LocalDevelopmentSchemaState.Existing)
        {
            return false;
        }

        if (schemaState == LocalDevelopmentSchemaState.Inconsistent)
        {
            throw new InvalidOperationException(
                "Local database is missing dbo.Organizations but still contains other Workslip tables. " +
                "Automatic bootstrap refused the partial schema instead of overwriting it. " +
                "Drop/recreate the disposable local database or restore a consistent local schema.");
        }

        // A failed historical-migration attempt can leave only
        // dbo.WorkslipSchemaMigrations behind. EnsureCreated intentionally refuses
        // to create model tables when any table exists, so create the current EF
        // model tables explicitly while preserving that harmless history table.
        var relationalDatabaseCreator = db.GetService<IRelationalDatabaseCreator>();
        await relationalDatabaseCreator.CreateTablesAsync(cancellationToken);

        await CompleteFreshSchemaAsync(connectionString, contentRootPath, cancellationToken);

        Log.Information(
            "[STARTUP 08.1] Fresh local database schema created from the current EF model after migration-history-only residue");
        return true;
    }

    // EnsureCreated/CreateTables only builds tables that belong to the EF model. Migration-
    // managed tables that are not modeled by EF (the Dapper-owned billing and knowledge-
    // document tables) must be created by running the migrations that introduce them before
    // the remaining migrations are recorded as an already-applied baseline. Running the
    // table-creating migrations first ensures the baseline does not mark them as applied
    // while the tables are still missing.
    private static async Task CompleteFreshSchemaAsync(
        string connectionString,
        string contentRootPath,
        CancellationToken cancellationToken)
    {
        await LocalDevelopmentDatabaseMigrationRunner.ApplyMissingTableMigrationsAsync(
            connectionString,
            contentRootPath,
            cancellationToken);

        await LocalDevelopmentDatabaseMigrationRunner.BaselineCurrentSchemaAsync(
            connectionString,
            contentRootPath,
            cancellationToken);
    }

    internal static LocalDevelopmentSchemaState ClassifySchemaState(
        IReadOnlyCollection<string> dboTables)
    {
        if (dboTables.Any(table =>
                table.Equals(CoreTableName, StringComparison.OrdinalIgnoreCase)))
        {
            return LocalDevelopmentSchemaState.Existing;
        }

        if (dboTables.All(table =>
                table.Equals(MigrationHistoryTableName, StringComparison.OrdinalIgnoreCase)))
        {
            return LocalDevelopmentSchemaState.Fresh;
        }

        return LocalDevelopmentSchemaState.Inconsistent;
    }

    private static async Task<IReadOnlyList<string>> GetDboTableNamesAsync(
        SqlDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT tableInfo.name
                FROM sys.tables AS tableInfo
                INNER JOIN sys.schemas AS schemaInfo
                    ON schemaInfo.schema_id = tableInfo.schema_id
                WHERE schemaInfo.name = N'dbo'
                  AND tableInfo.is_ms_shipped = 0
                ORDER BY tableInfo.name;
                """;

            var tableNames = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tableNames.Add(reader.GetString(0));
            }

            return tableNames;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}

internal enum LocalDevelopmentSchemaState
{
    Existing,
    Fresh,
    Inconsistent
}
