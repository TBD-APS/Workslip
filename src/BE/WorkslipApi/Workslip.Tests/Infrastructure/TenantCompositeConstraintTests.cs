using Microsoft.Data.Sqlite;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class TenantCompositeConstraintTests
{
    [Fact]
    public async Task Filial_relationships_reject_cross_organization_user_and_job()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE OrganizationFilials (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UNIQUE (OrganizationId, Id)
            );
            CREATE TABLE Users (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                FilialId TEXT NOT NULL,
                FOREIGN KEY (OrganizationId, FilialId)
                    REFERENCES OrganizationFilials (OrganizationId, Id)
            );
            CREATE TABLE JobReports (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                FilialId TEXT NOT NULL,
                FOREIGN KEY (OrganizationId, FilialId)
                    REFERENCES OrganizationFilials (OrganizationId, Id)
            );
            """);

        var firstOrganizationId = Guid.NewGuid();
        var secondOrganizationId = Guid.NewGuid();
        var firstFilialId = Guid.NewGuid();
        var secondFilialId = Guid.NewGuid();
        await InsertFilialAsync(connection, firstOrganizationId, firstFilialId);
        await InsertFilialAsync(connection, secondOrganizationId, secondFilialId);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO Users (Id, OrganizationId, FilialId) VALUES ($id, $organizationId, $filialId);",
            ("$id", Guid.NewGuid()),
            ("$organizationId", firstOrganizationId),
            ("$filialId", secondFilialId)));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO JobReports (Id, OrganizationId, FilialId) VALUES ($id, $organizationId, $filialId);",
            ("$id", Guid.NewGuid()),
            ("$organizationId", firstOrganizationId),
            ("$filialId", secondFilialId)));

        await ExecuteAsync(
            connection,
            "INSERT INTO Users (Id, OrganizationId, FilialId) VALUES ($id, $organizationId, $filialId);",
            ("$id", Guid.NewGuid()),
            ("$organizationId", firstOrganizationId),
            ("$filialId", firstFilialId));
    }

    [Fact]
    public async Task Installation_snapshot_relationships_reject_cross_organization_definitions()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE JobReportInstallations (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UNIQUE (OrganizationId, Id)
            );
            CREATE TABLE ControlCategories (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UNIQUE (OrganizationId, Id)
            );
            CREATE TABLE ControlPoints (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UNIQUE (OrganizationId, Id)
            );
            CREATE TABLE JobReportInstallationCategories (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                JobReportInstallationId TEXT NOT NULL,
                ControlCategoryId TEXT NOT NULL,
                UNIQUE (OrganizationId, Id),
                FOREIGN KEY (OrganizationId, JobReportInstallationId)
                    REFERENCES JobReportInstallations (OrganizationId, Id),
                FOREIGN KEY (OrganizationId, ControlCategoryId)
                    REFERENCES ControlCategories (OrganizationId, Id)
            );
            CREATE TABLE JobReportInstallationControlPoints (
                OrganizationId TEXT NOT NULL,
                JobReportInstallationCategoryId TEXT NOT NULL,
                ControlPointId TEXT NOT NULL,
                FOREIGN KEY (OrganizationId, JobReportInstallationCategoryId)
                    REFERENCES JobReportInstallationCategories (OrganizationId, Id),
                FOREIGN KEY (OrganizationId, ControlPointId)
                    REFERENCES ControlPoints (OrganizationId, Id)
            );
            """);

        var firstOrganizationId = Guid.NewGuid();
        var secondOrganizationId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var firstCategoryId = Guid.NewGuid();
        var secondCategoryId = Guid.NewGuid();
        var firstPointId = Guid.NewGuid();
        var secondPointId = Guid.NewGuid();

        await InsertTenantOwnedAsync(connection, "JobReportInstallations", installationId, firstOrganizationId);
        await InsertTenantOwnedAsync(connection, "ControlCategories", firstCategoryId, firstOrganizationId);
        await InsertTenantOwnedAsync(connection, "ControlCategories", secondCategoryId, secondOrganizationId);
        await InsertTenantOwnedAsync(connection, "ControlPoints", firstPointId, firstOrganizationId);
        await InsertTenantOwnedAsync(connection, "ControlPoints", secondPointId, secondOrganizationId);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO JobReportInstallationCategories
                (Id, OrganizationId, JobReportInstallationId, ControlCategoryId)
            VALUES ($id, $organizationId, $installationId, $controlCategoryId);
            """,
            ("$id", Guid.NewGuid()),
            ("$organizationId", firstOrganizationId),
            ("$installationId", installationId),
            ("$controlCategoryId", secondCategoryId)));

        var snapshotCategoryId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO JobReportInstallationCategories
                (Id, OrganizationId, JobReportInstallationId, ControlCategoryId)
            VALUES ($id, $organizationId, $installationId, $controlCategoryId);
            """,
            ("$id", snapshotCategoryId),
            ("$organizationId", firstOrganizationId),
            ("$installationId", installationId),
            ("$controlCategoryId", firstCategoryId));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            """
            INSERT INTO JobReportInstallationControlPoints
                (OrganizationId, JobReportInstallationCategoryId, ControlPointId)
            VALUES ($organizationId, $categoryId, $controlPointId);
            """,
            ("$organizationId", firstOrganizationId),
            ("$categoryId", snapshotCategoryId),
            ("$controlPointId", secondPointId)));
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static Task InsertFilialAsync(SqliteConnection connection, Guid organizationId, Guid filialId) =>
        ExecuteAsync(
            connection,
            "INSERT INTO OrganizationFilials (Id, OrganizationId) VALUES ($id, $organizationId);",
            ("$id", filialId),
            ("$organizationId", organizationId));

    private static Task InsertTenantOwnedAsync(
        SqliteConnection connection,
        string table,
        Guid id,
        Guid organizationId) =>
        ExecuteAsync(
            connection,
            $"INSERT INTO {table} (Id, OrganizationId) VALUES ($id, $organizationId);",
            ("$id", id),
            ("$organizationId", organizationId));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value.ToString()!);
        }

        await command.ExecuteNonQueryAsync();
    }
}
