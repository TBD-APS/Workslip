using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;

namespace Workslip.Tests.Jobs;

public sealed class EfJobAuditorScopeRepositoryTests
{
    [Fact]
    public async Task Scope_read_write_and_batch_queries_are_tenant_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
            .Options;
        await using var context = new SqlDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var filialA = Guid.NewGuid();
        var filialB = Guid.NewGuid();
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.Organizations.AddRange(
            CreateOrganization(organizationA, "Organization A", "12345678", now),
            CreateOrganization(organizationB, "Organization B", "87654321", now));
        context.Set<OrganizationFilialRow>().AddRange(
            CreateFilial(filialA, organizationA, "A", now),
            CreateFilial(filialB, organizationB, "B", now));
        context.JobReports.AddRange(
            CreateJob(jobA, organizationA, filialA, "0001", now),
            CreateJob(jobB, organizationB, filialB, "0002", now));
        await context.SaveChangesAsync();

        var repository = new EfJobAuditorScopeRepository(context);

        var ownInitial = await repository.GetAsync(jobA, organizationA, CancellationToken.None);
        var foreignRead = await repository.GetAsync(jobB, organizationA, CancellationToken.None);
        var foreignWrite = await repository.SetAsync(
            jobB,
            organizationA,
            false,
            "Should never cross tenant",
            CancellationToken.None);
        var visibleIds = await repository.GetVisibleJobIdsAsync(
            organizationA,
            [jobA, jobB],
            CancellationToken.None);
        var ownHidden = await repository.SetAsync(
            jobA,
            organizationA,
            false,
            "Intern sag",
            CancellationToken.None);
        var visibleAfterHide = await repository.GetVisibleJobIdsAsync(
            organizationA,
            [jobA, jobB],
            CancellationToken.None);

        Assert.NotNull(ownInitial);
        Assert.True(ownInitial.IsInAuditorScope);
        Assert.Null(foreignRead);
        Assert.Null(foreignWrite);
        Assert.Contains(jobA, visibleIds);
        Assert.DoesNotContain(jobB, visibleIds);
        Assert.NotNull(ownHidden);
        Assert.False(ownHidden.IsInAuditorScope);
        Assert.Equal("Intern sag", ownHidden.Reason);
        Assert.Empty(visibleAfterHide);

        var foreignPersisted = await context.JobReports
            .AsNoTracking()
            .SingleAsync(job => job.Id == jobB);
        Assert.True(foreignPersisted.IsInAuditorScope);
        Assert.Null(foreignPersisted.AuditorScopeReason);
    }

    private static OrganizationRow CreateOrganization(
        Guid id,
        string name,
        string cvr,
        DateTimeOffset now) => new()
    {
        Id = id,
        Name = name,
        Cvr = cvr,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static OrganizationFilialRow CreateFilial(
        Guid id,
        Guid organizationId,
        string name,
        DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Name = name,
        IsDefault = true,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static JobReportRow CreateJob(
        Guid id,
        Guid organizationId,
        Guid filialId,
        string reportNumber,
        DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        FilialId = filialId,
        ReportNumber = reportNumber,
        Status = JobStatus.Draft.ToString(),
        CreatedAt = now,
        UpdatedAt = now
    };
}
