using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Schema;

/// <summary>
/// Production schema-drift guard.
///
/// WHY THIS EXISTS: local development and every test build the database schema from the
/// EF model via <c>EnsureCreated</c>, while the PRODUCTION schema changes ONLY through the
/// hand-written SQL files in <c>src/BE/infrastructure/database/migrations</c>. Nothing else
/// compares the two, so a column added to the EF model without a matching migration works
/// locally and in CI but is silently missing in production until it fails at runtime.
///
/// This fixture pins the EF model against an explicit, reviewed schema contract. It fails
/// whenever the model and the contract diverge, which forces a migration and a contract
/// update to land together. See the "Schema contract guard" section in
/// <c>src/BE/infrastructure/database/migrations/README.md</c>.
/// </summary>
public sealed class SchemaModelContractTests
{
    // Every column the production database is expected to have for each EF-mapped table.
    // Column provenance (which migration established each non-baseline column) is asserted
    // separately by Migration_backed_schema_is_present_in_migration_files.
    //
    // Tables created outside the EF model (raw Dapper: UserBillingRates,
    // WorksheetBillingSnapshots, KnowledgeDocuments, KnowledgeDocumentAttachments) are NOT
    // listed here because they are not part of context.Model; they are covered by the
    // migration-backed table check below.
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedColumnsByTable =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Organizations"] = new[] { "Id", "Name", "Cvr", "CreatedAt", "UpdatedAt" },
            ["OrganizationFilials"] = new[] { "Id", "OrganizationId", "Name", "IsDefault", "CreatedAt", "UpdatedAt" },
            ["Users"] = new[]
            {
                "Id", "OrganizationId", "FilialId", "Email", "DisplayName", "EntraId", "EntraEmail",
                "Phone", "Role", "UserKind", "CreatedAt", "UpdatedAt",
            },
            ["Customers"] = new[]
            {
                "Id", "OrganizationId", "CustomerNumber", "Name", "Address", "ZipCode", "City",
                "Country", "Email", "ContactPerson", "Phone", "IsFavorite", "CreatedAt", "UpdatedAt",
            },
            ["JobWorkKinds"] = new[] { "Id", "NormalizedLabel", "Label", "RequiresCustomWorkKind", "IsActive", "SortOrder" },
            ["JobClosureFlags"] = new[] { "Id", "NormalizedLabel", "Label", "IsExclusive", "IsActive", "SortOrder", "UpdatedAt" },
            ["JobReportClosureFlags"] = new[] { "Id", "JobReportId", "OrganizationId", "ClosureFlagId", "SortOrder" },
            ["JobReports"] = new[]
            {
                "Id", "OrganizationId", "FilialId", "CustomerId", "CustomerName", "CustomerEmail",
                "CustomerPhone", "CustomerAddress", "CustomerContactPerson", "DestinationAddress",
                "DestinationZipCode", "DestinationCity", "ReportNumber", "Status", "JobType", "ReportDate",
                "TaskDescription", "CustomerObservations", "TechnicalObservations", "WorkKindId",
                "CustomWorkKind", "Remarks", "IsSoftDeleted", "CreatedAt", "UpdatedAt", "DeletionScheduledAt",
                "SubmittedAt", "SubmittedByUserId", "RejectionNote", "IsInAuditorScope", "AuditorScopeReason",
            },
            ["JobAssignments"] = new[] { "Id", "OrganizationId", "ReportId", "UserId", "AssignedByUserId", "AssignedAt" },
            // JobReportRowId is an EF shadow foreign key produced by the unmapped
            // JobReportRow.Links collection navigation (it has no explicitly configured FK).
            // It is part of the model EnsureCreated builds, so it belongs in the contract;
            // it is a likely-unintended shadow column worth cleaning up separately.
            ["JobReportLinks"] = new[] { "Id", "OrganizationId", "SourceReportId", "TargetReportId", "CreatedAt", "JobReportRowId" },
            ["JobEvents"] = new[]
            {
                "Id", "OrganizationId", "ReportId", "ActorId", "EventType", "Summary", "BeforeJson", "AfterJson", "CreatedAt",
            },
            ["InviteTokens"] = new[]
            {
                "Id", "OrganizationId", "Email", "Token", "Role", "UserKind", "ExpiresAt", "Consumed",
                "CreatedAt", "OpenedAt", "AcceptedAt", "RevokedAt", "EntraUserId", "EntraEmail",
                "EntraCreatedByInvite", "EntraProvisionedAt", "EntraCleanedAt",
            },
            ["Worksheets"] = new[]
            {
                "Id", "OrganizationId", "JobId", "UserId", "WorkDate", "HoursWorked", "SleptOnJob", "CreatedAt", "UpdatedAt",
            },
            ["JobReportInstallations"] = new[] { "Id", "JobReportId", "OrganizationId", "InstallationTypeDefinitionId", "SortOrder" },
            ["ControlCategories"] = new[] { "Id", "OrganizationId", "Name", "SortOrder" },
            ["ControlPoints"] = new[] { "Id", "OrganizationId", "Name", "IsActive", "SortOrder" },
            ["JobReportInstallationCategories"] = new[]
            {
                "Id", "OrganizationId", "JobReportInstallationId", "ControlCategoryId", "SortOrder", "IsIrrelevant",
            },
            ["JobReportInstallationControlPoints"] = new[]
            {
                "OrganizationId", "JobReportInstallationCategoryId", "ControlPointId", "SortOrder", "IsRequired", "IsChecked",
            },
            ["InstallationTypeDefinitions"] = new[] { "Id", "OrganizationId", "Name", "SortOrder" },
            ["InstallationTypeDefinitionMappings"] = new[]
            {
                "InstallationTypeDefinitionId", "ControlCategoryId", "ControlPointId", "SortOrder", "IsRequired",
            },
            ["PushSubscriptions"] = new[]
            {
                "Id", "UserId", "Endpoint", "P256Dh", "Auth", "UserAgent", "IsActive", "CreatedUtc", "LastSeenUtc",
            },
            ["NotificationQueue"] = new[]
            {
                "Id", "UserId", "NotificationType", "PayloadJson", "Status", "RetryCount", "CreatedUtc",
                "ProcessingStartedUtc", "NextAttemptUtc", "CompletedUtc", "ReadUtc", "LastError",
            },
            ["NotificationDeliveryLog"] = new[] { "Id", "NotificationId", "SubscriptionId", "Success", "SentUtc", "ErrorMessage" },
            ["JobViews"] = new[] { "Id", "JobId", "UserId", "ViewType", "ViewedAt" },
            ["IdempotencyRecords"] = new[]
            {
                "Id", "Scope", "Key", "RequestHash", "ReservationToken", "Completed", "StatusCode",
                "ResponseJson", "CreatedAt", "ExpiresAt",
            },
        };

    // Columns added to pre-existing tables after the explicit-migration cutover. Each must be
    // established by the referenced migration file (matched by filename fragment).
    private static readonly (string Table, string Column, string MigrationFragment)[] MigrationBackedColumns =
        new[]
        {
            ("Users", "FilialId", "wor385"),
            ("JobReports", "FilialId", "wor385"),
            ("JobReportInstallationCategories", "OrganizationId", "wor385"),
            ("JobReportInstallationControlPoints", "OrganizationId", "wor385"),
            ("Users", "UserKind", "wor412"),
            ("InviteTokens", "UserKind", "wor412"),
            ("JobReports", "IsInAuditorScope", "wor479"),
            ("JobReports", "AuditorScopeReason", "wor479"),
        };

    // Tables created after the cutover. Each must be created by the referenced migration file.
    private static readonly (string Table, string MigrationFragment)[] MigrationCreatedTables =
        new[]
        {
            ("OrganizationFilials", "wor385"),
            ("UserBillingRates", "wor428"),
            ("WorksheetBillingSnapshots", "wor428"),
            ("KnowledgeDocuments", "wor455"),
            ("KnowledgeDocumentAttachments", "wor455"),
            ("JobConversationMessages", "wor551"),
            ("JobConversationReads", "wor551"),
        };

    [Fact]
    public void EfModel_matches_production_schema_contract()
    {
        var modelColumns = GetModelColumnsByTable();

        var modelTables = new HashSet<string>(modelColumns.Keys, StringComparer.Ordinal);
        var contractTables = new HashSet<string>(ExpectedColumnsByTable.Keys, StringComparer.Ordinal);

        var messages = new List<string>();

        var modelOnlyTables = modelTables.Except(contractTables).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (modelOnlyTables.Count > 0)
        {
            messages.Add(
                "Tables in the EF model but NOT in the schema contract (add a migration that creates them, then add them here): "
                + string.Join(", ", modelOnlyTables));
        }

        var contractOnlyTables = contractTables.Except(modelTables).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (contractOnlyTables.Count > 0)
        {
            messages.Add(
                "Tables in the schema contract but NOT in the EF model (stale contract, or a removed table that still needs a migration): "
                + string.Join(", ", contractOnlyTables));
        }

        foreach (var table in modelTables.Intersect(contractTables).OrderBy(x => x, StringComparer.Ordinal))
        {
            var actual = modelColumns[table];
            var expected = new HashSet<string>(ExpectedColumnsByTable[table], StringComparer.Ordinal);

            var modelOnly = actual.Except(expected).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (modelOnly.Count > 0)
            {
                messages.Add(
                    $"  {table}: columns in the EF model but NOT in the contract "
                    + $"(each needs a migration in src/BE/infrastructure/database/migrations, then add it here): "
                    + string.Join(", ", modelOnly));
            }

            var contractOnly = expected.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (contractOnly.Count > 0)
            {
                messages.Add(
                    $"  {table}: columns in the contract but NOT in the EF model "
                    + $"(stale contract, or a removed column that still needs a migration): "
                    + string.Join(", ", contractOnly));
            }
        }

        Assert.True(
            messages.Count == 0,
            "The EF model and the production schema contract have drifted apart. Production schema changes only "
            + "through src/BE/infrastructure/database/migrations, so every difference below must be reconciled by a "
            + "migration AND an update to ExpectedColumnsByTable in this file:\n"
            + string.Join("\n", messages));
    }

    [Fact]
    public void Migration_backed_schema_is_present_in_migration_files()
    {
        var migrationsDirectory = FindMigrationsDirectory();
        var migrationContentById = Directory
            .GetFiles(migrationsDirectory, "*.sql")
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase);

        var problems = new List<string>();

        foreach (var (table, column, fragment) in MigrationBackedColumns)
        {
            var matches = migrationContentById
                .Where(pair => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                problems.Add($"No migration file name contains '{fragment}' for column {table}.{column}.");
            }
            else if (!matches.Any(pair => pair.Value.Contains(column, StringComparison.Ordinal)))
            {
                problems.Add($"No '{fragment}' migration mentions column '{column}' expected on table {table}.");
            }
        }

        foreach (var (table, fragment) in MigrationCreatedTables)
        {
            var matches = migrationContentById
                .Where(pair => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                problems.Add($"No migration file name contains '{fragment}' for table {table}.");
            }
            else if (!matches.Any(pair => pair.Value.Contains(table, StringComparison.Ordinal)))
            {
                problems.Add($"No '{fragment}' migration creates or mentions table '{table}'.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Schema contract references migration-backed tables/columns that are not present in their migration files:\n"
            + string.Join("\n", problems));
    }

    private static Dictionary<string, HashSet<string>> GetModelColumnsByTable()
    {
        // Only the model is needed, not a live database, so no connection is opened.
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new SqlDbContext(options);

        var columnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            if (!columnsByTable.TryGetValue(tableName, out var columns))
            {
                columns = new HashSet<string>(StringComparer.Ordinal);
                columnsByTable[tableName] = columns;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (columnName is not null)
                {
                    columns.Add(columnName);
                }
            }
        }

        return columnsByTable;
    }

    private static string FindMigrationsDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "BE", "infrastructure", "database", "migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/BE/infrastructure/database/migrations by walking up from "
            + AppContext.BaseDirectory);
    }
}
