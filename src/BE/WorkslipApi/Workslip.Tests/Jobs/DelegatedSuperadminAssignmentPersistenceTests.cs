using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Jobs;

public sealed class DelegatedSuperadminAssignmentPersistenceTests
{
    [Fact]
    public async Task Initial_assignment_does_not_store_platform_superadmin_in_tenant_user_foreign_key()
    {
        var fixture = await CreateFixtureAsync(JobStatus.Draft);
        await using var connection = fixture.Connection;
        await using var context = fixture.Context;

        var repository = new EfAssignmentRepository(context, null!, fixture.CurrentUser, null!, null!);

        await repository.AddAssignedUsersAsync(
            fixture.TenantOrganizationId,
            fixture.JobId,
            [fixture.AssignedUserId],
            fixture.SuperadminId,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await context.SaveChangesAsync();

        var assignment = await context.JobAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.AssignedUserId, assignment.UserId);
        Assert.Null(assignment.AssignedByUserId);
    }

    [Fact]
    public async Task Assignment_audit_does_not_store_platform_superadmin_in_tenant_user_foreign_key()
    {
        var fixture = await CreateFixtureAsync(JobStatus.InReview);
        await using var connection = fixture.Connection;
        await using var context = fixture.Context;

        var repository = new EfAssignmentRepository(context, null!, fixture.CurrentUser, null!, null!);

        await repository.AddAssignedUsersAsync(
            fixture.TenantOrganizationId,
            fixture.JobId,
            [fixture.AssignedUserId],
            fixture.SuperadminId,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await context.SaveChangesAsync();

        var assignment = await context.JobAssignments.AsNoTracking().SingleAsync();
        Assert.Null(assignment.AssignedByUserId);

        var audit = await context.JobEvents.AsNoTracking().SingleAsync(e => e.ReportId == fixture.JobId);
        Assert.Null(audit.ActorId);
        Assert.Equal("Tech Tim tilføjet", audit.Summary);
    }

    private static async Task<Fixture> CreateFixtureAsync(JobStatus status)
    {
        var tenantOrganizationId = Guid.NewGuid();
        var platformOrganizationId = Guid.NewGuid();
        var superadminId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var currentUser = new TestCurrentUserContext(superadminId, tenantOrganizationId, Roles.Superadmin);

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditInterceptor(currentUser))
            .Options;
        var context = new SqlDbContext(options);

        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        await CreateMinimalRelationalSchemaAsync(context);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Users (Id, OrganizationId, DisplayName)
            VALUES ({superadminId}, {platformOrganizationId}, {"Platform Superadmin"});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Users (Id, OrganizationId, DisplayName)
            VALUES ({assignedUserId}, {tenantOrganizationId}, {"Tech Tim"});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO JobReports (Id, OrganizationId, ReportNumber, Status)
            VALUES ({jobId}, {tenantOrganizationId}, {"JOB-1"}, {status.ToString()});
            """);

        return new Fixture(
            connection,
            context,
            currentUser,
            tenantOrganizationId,
            superadminId,
            assignedUserId,
            jobId);
    }

    private static Task CreateMinimalRelationalSchemaAsync(SqlDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Users (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                CONSTRAINT PK_Users PRIMARY KEY (Id),
                CONSTRAINT AK_Users_Organization_Id UNIQUE (OrganizationId, Id)
            );

            CREATE TABLE JobReports (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                ReportNumber TEXT NULL,
                Status TEXT NOT NULL,
                CONSTRAINT PK_JobReports PRIMARY KEY (Id),
                CONSTRAINT AK_JobReports_Organization_Id UNIQUE (OrganizationId, Id)
            );

            CREATE TABLE JobAssignments (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                ReportId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                AssignedByUserId TEXT NULL,
                AssignedAt TEXT NOT NULL,
                CONSTRAINT PK_JobAssignments PRIMARY KEY (Id),
                CONSTRAINT FK_JobAssignments_Report FOREIGN KEY (OrganizationId, ReportId)
                    REFERENCES JobReports (OrganizationId, Id) ON DELETE CASCADE,
                CONSTRAINT FK_JobAssignments_User FOREIGN KEY (OrganizationId, UserId)
                    REFERENCES Users (OrganizationId, Id) ON DELETE RESTRICT,
                CONSTRAINT FK_JobAssignments_AssignedBy FOREIGN KEY (OrganizationId, AssignedByUserId)
                    REFERENCES Users (OrganizationId, Id) ON DELETE RESTRICT
            );

            CREATE TABLE JobEvents (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                ReportId TEXT NOT NULL,
                ActorId TEXT NULL,
                EventType TEXT NOT NULL,
                Summary TEXT NULL,
                BeforeJson TEXT NULL,
                AfterJson TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CONSTRAINT PK_JobEvents PRIMARY KEY (Id),
                CONSTRAINT FK_JobEvents_Report FOREIGN KEY (OrganizationId, ReportId)
                    REFERENCES JobReports (OrganizationId, Id) ON DELETE CASCADE,
                CONSTRAINT FK_JobEvents_Actor FOREIGN KEY (OrganizationId, ActorId)
                    REFERENCES Users (OrganizationId, Id) ON DELETE RESTRICT
            );
            """);

    private sealed record Fixture(
        SqliteConnection Connection,
        SqlDbContext Context,
        TestCurrentUserContext CurrentUser,
        Guid TenantOrganizationId,
        Guid SuperadminId,
        Guid AssignedUserId,
        Guid JobId);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;
}
