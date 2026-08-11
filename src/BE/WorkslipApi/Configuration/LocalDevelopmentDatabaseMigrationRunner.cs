using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Workslip.Api.Configuration;

internal static partial class LocalDevelopmentDatabaseMigrationRunner
{
    private const string MigrationLockName = "Workslip.SchemaMigrations";
    private const string MigrationHistoryTable = "dbo.WorkslipSchemaMigrations";
    private const string Wor385FilialTenantIntegrityMigrationId = "20260809_1145_wor385_filial_tenant_integrity";

    [GeneratedRegex(@"^\d{8}_\d{4}_[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationIdPattern();

    [GeneratedRegex(@"(?im)^\s*GO\s*(?:--.*)?$")]
    private static partial Regex GoBatchSeparatorPattern();

    public static bool IsLocalSqlTarget(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return IsLocalDataSource(builder.DataSource);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsLocalDataSource(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            return false;

        var normalized = dataSource.Trim();
        if (normalized.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..].Trim();

        if (normalized.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase))
            return true;

        string host;
        if (normalized.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = normalized.IndexOf(']');
            host = closingBracket > 0 ? normalized[1..closingBracket] : normalized;
        }
        else
        {
            var separatorIndex = normalized.IndexOfAny([',', '\\']);
            host = separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
        }

        return host.Trim() switch
        {
            "." => true,
            var value when value.Equals("(local)", StringComparison.OrdinalIgnoreCase) => true,
            var value when value.Equals("localhost", StringComparison.OrdinalIgnoreCase) => true,
            "127.0.0.1" => true,
            "::1" => true,
            _ => false
        };
    }

    public static async Task ApplyPendingAsync(
        string connectionString,
        string contentRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocalSqlTarget(connectionString))
        {
            throw new InvalidOperationException(
                "Local development migrations refused the configured SQL target because it is not provably local.");
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

        var appliedCount = 0;
        foreach (var migration in migrations)
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
                            "[STARTUP 08.1] Reconciled local migration checksum: {MigrationId}",
                            migration.Id);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Applied local migration '{migration.Id}' has checksum '{storedChecksum}', " +
                        $"but this branch contains '{migration.Sha256}'. Applied migrations are immutable.");
                }

                await RepairLegacyLocalSchemaPrerequisitesAsync(
                    connection,
                    transaction,
                    migration.Id,
                    cancellationToken);
                await ExecuteMigrationAsync(connection, transaction, migration, cancellationToken);
                await RecordMigrationAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                appliedCount++;
                Log.Information(
                    "[STARTUP 08.1] Applied local database migration: {MigrationId}",
                    migration.Id);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        Log.Information(
            "[STARTUP 08.1] Local database migrations complete. Applied: {AppliedCount}, TotalKnown: {MigrationCount}",
            appliedCount,
            migrations.Count);
    }

    internal static IReadOnlyList<LocalMigration> LoadMigrations(string migrationsPath)
    {
        if (!Directory.Exists(migrationsPath))
            throw new DirectoryNotFoundException($"Migration directory not found: {migrationsPath}");

        var migrations = new List<LocalMigration>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(migrationsPath, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!MigrationIdPattern().IsMatch(id))
                throw new InvalidOperationException($"Invalid migration filename '{Path.GetFileName(path)}'. Expected YYYYMMDD_HHMM_slug.sql.");
            if (!seenIds.Add(id))
                throw new InvalidOperationException($"Duplicate migration ID '{id}'.");

            var sql = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"Migration '{Path.GetFileName(path)}' is empty.");
            if (GoBatchSeparatorPattern().IsMatch(sql))
                throw new InvalidOperationException(
                    $"Migration '{Path.GetFileName(path)}' contains a GO batch separator. Workslip migrations must be one transaction-safe T-SQL batch.");

            var checksum = GetMigrationChecksumInfo(path, sql);
            migrations.Add(new LocalMigration(id, sql, checksum.CanonicalSha256, checksum.LegacySha256));
        }

        return migrations;
    }

    private static async Task EnsureMigrationHistoryTableAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireMigrationLockAsync(connection, transaction, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 120;
            command.CommandText = $"""
                IF OBJECT_ID(N'{MigrationHistoryTable}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {MigrationHistoryTable}
                    (
                        MigrationId nvarchar(200) NOT NULL,
                        Sha256 char(64) NOT NULL,
                        AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_WorkslipSchemaMigrations_AppliedAt DEFAULT sysutcdatetime(),
                        AppliedBy nvarchar(200) NULL,
                        CONSTRAINT PK_WorkslipSchemaMigrations PRIMARY KEY (MigrationId)
                    );
                END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task AcquireMigrationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 65;
        command.CommandText = """
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 60000;
            SELECT @lockResult;
            """;
        command.Parameters.AddWithValue("@resource", MigrationLockName);

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
            throw new InvalidOperationException("Could not acquire the Workslip local schema migration lock.");
    }

    private static async Task<string?> GetStoredChecksumAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = $"SELECT Sha256 FROM {MigrationHistoryTable} WITH (UPDLOCK, HOLDLOCK) WHERE MigrationId = @migrationId;";
        command.Parameters.AddWithValue("@migrationId", migrationId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result)?.Trim().ToLowerInvariant();
    }

    private static async Task UpdateStoredChecksumAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string migrationId,
        string oldChecksum,
        string newChecksum,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = $"""
            UPDATE {MigrationHistoryTable}
            SET Sha256 = @newChecksum
            WHERE MigrationId = @migrationId
              AND Sha256 = @oldChecksum;
            """;
        command.Parameters.AddWithValue("@migrationId", migrationId);
        command.Parameters.AddWithValue("@oldChecksum", oldChecksum);
        command.Parameters.AddWithValue("@newChecksum", newChecksum);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Local migration checksum reconciliation failed for '{migrationId}'.");
    }

    private static async Task RepairLegacyLocalSchemaPrerequisitesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        if (!migrationId.Equals(Wor385FilialTenantIntegrityMigrationId, StringComparison.OrdinalIgnoreCase))
            return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 120;
        command.CommandText = """
            DECLARE @repaired bit = 0;

            IF OBJECT_ID(N'dbo.OrganizationFilials', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.key_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.OrganizationFilials')
                     AND name = N'AK_OrganizationFilials_OrganizationId_Id')
            BEGIN
                IF COL_LENGTH(N'dbo.OrganizationFilials', N'OrganizationId') IS NULL
                   OR COL_LENGTH(N'dbo.OrganizationFilials', N'Id') IS NULL
                BEGIN
                    THROW 51410, 'Local WOR-385 compatibility repair requires dbo.OrganizationFilials.OrganizationId and Id.', 1;
                END;

                ALTER TABLE dbo.OrganizationFilials
                    ADD CONSTRAINT AK_OrganizationFilials_OrganizationId_Id
                    UNIQUE (OrganizationId, Id);

                SET @repaired = 1;
            END;

            SELECT @repaired;
            """;

        var repaired = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        if (repaired)
        {
            Log.Warning(
                "[STARTUP 08.1] Repaired legacy local schema prerequisite before migration: {MigrationId}",
                migrationId);
        }
    }

    private static async Task ExecuteMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        LocalMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 600;
        command.CommandText = migration.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordMigrationAsync(
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
            VALUES (@migrationId, @sha256, N'local-dev');
            """;
        command.Parameters.AddWithValue("@migrationId", migration.Id);
        command.Parameters.AddWithValue("@sha256", migration.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MigrationChecksumInfo GetMigrationChecksumInfo(string path, string sql)
    {
        var normalizedSql = sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var canonicalChecksum = GetSha256Hex(utf8NoBom.GetBytes(normalizedSql));
        var rawChecksum = GetSha256Hex(File.ReadAllBytes(path));
        var crlfChecksum = GetSha256Hex(utf8NoBom.GetBytes(normalizedSql.Replace("\n", "\r\n", StringComparison.Ordinal)));

        var legacyChecksums = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            rawChecksum,
            crlfChecksum
        };
        legacyChecksums.Remove(canonicalChecksum);

        return new MigrationChecksumInfo(canonicalChecksum, legacyChecksums);
    }

    private static string GetSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed record LocalMigration(
        string Id,
        string Sql,
        string Sha256,
        IReadOnlySet<string> LegacySha256);

    private sealed record MigrationChecksumInfo(
        string CanonicalSha256,
        IReadOnlySet<string> LegacySha256);
}
