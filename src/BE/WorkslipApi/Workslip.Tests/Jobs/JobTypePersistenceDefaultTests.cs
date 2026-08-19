using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;

namespace Workslip.Tests.Jobs;

public sealed class JobTypePersistenceDefaultTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_defaults_missing_or_blank_job_type_to_kls(string? jobType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateFunction(
            "sysutcdatetime",
            () => DateTimeOffset.UtcNow.ToString("O"),
            isDeterministic: false);
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(
                SqliteSchemaCompatibilityInterceptor.Instance,
                new TenantIntegrityInterceptor())
            .Options;
        await using var context = new SqlDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Organizations.Add(new OrganizationRow
        {
            Id = organizationId,
            Name = "Test organization",
            Cvr = "12345678",
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Users.Add(new UserDataRow
        {
            Id = adminId,
            OrganizationId = organizationId,
            Email = "admin@example.test",
            DisplayName = "Test admin",
            Role = Roles.Admin,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        var currentUser = new TestCurrentUserContext(adminId, organizationId, Roles.Admin);
        var retryPolicy = new NoRetryPolicy();
        var worksheetRepository = new EfWorksheetRepository(context, currentUser, retryPolicy);
        var jobViewRepository = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
        var assignmentRepository = new EfAssignmentRepository(
            context,
            retryPolicy,
            currentUser,
            worksheetRepository,
            jobViewRepository);
        var repository = new EfJobRepository(
            context,
            retryPolicy,
            new EfCustomerRepository(context, retryPolicy),
            assignmentRepository,
            new EfJobLinkRepository(context, retryPolicy),
            worksheetRepository,
            jobViewRepository);

        var created = await repository.CreateAsync(
            organizationId,
            new CreateJobRequest(JobType: jobType),
            [],
            adminId,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context.JobReports
            .AsNoTracking()
            .SingleAsync(job => job.Id == created.Id);

        Assert.Equal(JobType.KLS, persisted.JobType);
        Assert.Equal(JobType.KLS, created.JobType);
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
