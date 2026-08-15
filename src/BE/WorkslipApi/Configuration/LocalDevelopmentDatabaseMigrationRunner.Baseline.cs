using Microsoft.Data.SqlClient;
using Serilog;

namespace Workslip.Api.Configuration;

internal static partial class LocalDevelopmentDatabaseMigrationRunner
{
    // Fresh local bootstrap was introduced after these historical migrations were already
    // represented by the then-current EF model. Only that fixed historical prefix is safe
    // to record as an EF baseline without executing the SQL. Any later migration must stay
    // pending so the normal migration runner executes it, including column/FK/data-only
    // migrations that cannot be detected by looking for CREATE TABLE statements.
    internal const string FreshEfBaselineThroughMigrationId =
        "20260810_2115_wor412_internal_test_user_audience";

    internal static bool BelongsToFreshEfBaseline(string migrationId) =>
        StringComparer.Ordinal.Compare(migrationId, FreshEfBaselineThroughMigrationId) <= 0;

    public static async Task BaselineCurrentSchemaAsync(
        string connectionString,
        string contentRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalSqlTarget(connectionString))
        {
            throw new InvalidOperationException(
                "Local schema baseline refused the configured SQL target because it is not provably local.");
        }

        var migrationsPath = Path.GetFullPath(Path.Combine(
            contentRootPath,
            "..",
            "infrastructure",
            "database",
            "migrations"));
        var migrations = LoadMigrations(migrationsPath);
        var baselineMigrations = migrations
            .Where(migration => BelongsToFreshEfBaseline(migration.Id))
            .ToArray();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureMigrationHistoryTableAsync(connection, cancellationToken);

        var recordedCount = 0;
        foreach (var migration in baselineMigrations)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireMigrationLockAsync(connection, transaction, cancellationToken);
                var storedChecksum = await GetStoredChecksumAsync(
                    connection,
                    transaction,
                    migration.Id,
                    cancellationToken);

                if (storedChecksum is not null)
                {
                    if (storedChecksum.Equals(migration.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    if (migration.LegacySha256.Contains(storedChecksum))
                    {
                        await UpdateStoredChecksumAsync(
                            connection,
                            transaction,
                            migration.Id,
                            storedChecksum,
                            migration.Sha256,
                            cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        Log.Information(
                            "[STARTUP 08.1] Reconciled local baseline migration checksum: {MigrationId}",
                            migration.Id);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Local schema baseline cannot record migration '{migration.Id}' because history contains " +
                        $"checksum '{storedChecksum}' instead of '{migration.Sha256}'.");
                }

                await RecordBaselineMigrationAsync(
                    connection,
                    transaction,
                    migration,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                recordedCount++;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        Log.Information(
            "[STARTUP 08.1] Fresh local schema migration baseline recorded. Added: {RecordedCount}, BaselineKnown: {BaselineCount}, TotalKnown: {MigrationCount}",
            recordedCount,
            baselineMigrations.Length,
            migrations.Count);
    }

    private static async Task RecordBaselineMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        LocalMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = $"""
            INSERT INTO {MigrationHistoryTable} (MigrationId, Sha256, AppliedBy)
            VALUES (@migrationId, @sha256, N'local-ef-baseline');
            """;
        command.Parameters.AddWithValue("@migrationId", migration.Id);
        command.Parameters.AddWithValue("@sha256", migration.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
