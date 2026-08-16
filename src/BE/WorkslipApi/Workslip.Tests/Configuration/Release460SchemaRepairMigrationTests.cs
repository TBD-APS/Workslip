using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class Release460SchemaRepairMigrationTests
{
    private const string RepairMigrationId = "20260815_2340_wor560_release_460_schema_repair";

    [Fact]
    public void RepairMigration_IsNeverPartOfFreshEfBaseline()
    {
        Assert.False(LocalDevelopmentDatabaseMigrationRunner.BelongsToFreshEfBaseline(RepairMigrationId));
    }

    [Fact]
    public void RepairMigration_CoversEveryKnownPostBaselineSchemaArtifact()
    {
        var path = FindMigrationPath();
        var sql = File.ReadAllText(path);

        var requiredArtifacts = new[]
        {
            "UserBillingRates",
            "WorksheetBillingSnapshots",
            "FK_WorksheetBillingSnapshots_Worksheets",
            "KnowledgeDocuments",
            "KnowledgeDocumentAttachments",
            "IsInAuditorScope",
            "AuditorScopeReason",
            "UserKind"
        };

        foreach (var artifact in requiredArtifacts)
        {
            Assert.Contains(artifact, sql, StringComparison.Ordinal);
        }
    }

    private static string FindMigrationPath()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "BE",
                "infrastructure",
                "database",
                "migrations",
                $"{RepairMigrationId}.sql");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate migration {RepairMigrationId}.sql.");
    }
}
