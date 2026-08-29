using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class ExplicitSchemaBaselineMigrationTests
{
    private const string BaselineMigrationId = "20260808_2359_workslip_explicit_schema_baseline";
    private const string FirstForwardMigrationId = "20260809_1145_wor385_filial_tenant_integrity";

    [Fact]
    public void Baseline_is_the_first_production_migration_and_precedes_WOR385()
    {
        var migrationIds = Directory
            .GetFiles(FindMigrationsDirectory(), "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(BaselineMigrationId, migrationIds[0]);
        Assert.True(
            Array.IndexOf(migrationIds, BaselineMigrationId) < Array.IndexOf(migrationIds, FirstForwardMigrationId),
            "The explicit baseline must run before the first migration that requires the pre-cutover Workslip schema.");
    }

    [Fact]
    public void Baseline_creates_the_WOR385_prerequisites_and_refuses_partial_schema()
    {
        var sql = File.ReadAllText(Path.Combine(FindMigrationsDirectory(), $"{BaselineMigrationId}.sql"));

        var wor385Prerequisites = new[]
        {
            "Organizations",
            "Users",
            "JobReports",
            "JobReportInstallations",
            "JobReportInstallationCategories",
            "JobReportInstallationControlPoints",
            "ControlCategories",
            "ControlPoints",
        };

        foreach (var table in wor385Prerequisites)
        {
            Assert.Contains($"CREATE TABLE [dbo].[{table}]", sql, StringComparison.Ordinal);
        }

        Assert.Contains("IF @presentBaselineTableCount = 0", sql, StringComparison.Ordinal);
        Assert.Contains("partial Workslip schema", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\nGO\n", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMigrationsDirectory()
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
                "migrations");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Workslip migration directory.");
    }
}
