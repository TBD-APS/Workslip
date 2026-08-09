using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
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

        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");

        var now = DateTimeOffset.UtcNow;
        context.IsSeeding = true;
        context.Organizations.AddRange(
            new OrganizationRow
            {
                Id = tenantOrganizationId,
                Name = "Tenant",
                Cvr = "11111111",
                CreatedAt = now,
                UpdatedAt = now
            },
            new OrganizationRow
            {
                Id = platformOrganizationId,
                Name = "Platform",
                Cvr = "22222222",
                CreatedAt = now,
                UpdatedAt = now
            });
        context.Users.AddRange(
            new UserDataRow
            {
                Id = superadminId,
                OrganizationId = platformOrganizationId,
                Email = "superadmin@example.test",
                DisplayName = "Platform Superadmin",
                EntraId = "superadmin-entra",
                EntraEmail = "superadmin@example.test",
                Phone = string.Empty,
                Role = Roles.Superadmin,
                CreatedAt = now,
                UpdatedAt = now
            },
            new UserDataRow
            {
                Id = assignedUserId,
                OrganizationId = tenantOrganizationId,
                Email = "tech@example.test",
                DisplayName = "Tech Tim",
                EntraId = "tech-entra",
                EntraEmail = "tech@example.test",
                Phone = string.Empty,
                Role = Roles.User,
                CreatedAt = now,
                UpdatedAt = now
            });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = tenantOrganizationId,
            ReportNumber = "JOB-1",
            Status = status.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        return new Fixture(
            connection,
            context,
            currentUser,
            tenantOrganizationId,
            superadminId,
            assignedUserId,
            jobId);
    }

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
