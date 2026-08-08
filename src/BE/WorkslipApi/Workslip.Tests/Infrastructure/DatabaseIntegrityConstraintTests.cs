using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DatabaseIntegrityConstraintTests
{
    private static int nextTestCvr = 12_345_670;

    [Fact]
    public void Model_uses_required_user_and_tenant_scoped_foreign_keys()
    {
        using var context = CreateModelContext();

        AssertForeignKey<PushSubscriptionRow, UserDataRow>(
            context,
            [nameof(PushSubscriptionRow.UserId)],
            [nameof(UserDataRow.Id)]);
        AssertForeignKey<NotificationQueueRow, UserDataRow>(
            context,
            [nameof(NotificationQueueRow.UserId)],
            [nameof(UserDataRow.Id)]);
        AssertForeignKey<JobViewRow, UserDataRow>(
            context,
            [nameof(JobViewRow.UserId)],
            [nameof(UserDataRow.Id)]);
        AssertForeignKey<WorksheetRow, JobReportRow>(
            context,
            [nameof(WorksheetRow.OrganizationId), nameof(WorksheetRow.JobId)],
            [nameof(JobReportRow.OrganizationId), nameof(JobReportRow.Id)]);
        AssertForeignKey<WorksheetRow, UserDataRow>(
            context,
            [nameof(WorksheetRow.OrganizationId), nameof(WorksheetRow.UserId)],
            [nameof(UserDataRow.OrganizationId), nameof(UserDataRow.Id)]);
        AssertForeignKey<JobReportRow, CustomerRow>(
            context,
            [nameof(JobReportRow.OrganizationId), nameof(JobReportRow.CustomerId)],
            [nameof(CustomerRow.OrganizationId), nameof(CustomerRow.Id)]);
    }

    [Theory]
    [InlineData("push-subscription")]
    [InlineData("notification")]
    [InlineData("job-view")]
    public async Task User_owned_rows_reject_missing_users(string dependentType)
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var job = await SeedTenantAsync(database.Context);
        var missingUserId = Guid.NewGuid();

        switch (dependentType)
        {
            case "push-subscription":
                database.Context.PushSubscriptions.Add(new PushSubscriptionRow
                {
                    Id = Guid.NewGuid(),
                    UserId = missingUserId,
                    Endpoint = "https://push.example.test/endpoint",
                    P256Dh = "p256dh",
                    Auth = "auth",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastSeenUtc = DateTimeOffset.UtcNow
                });
                break;
            case "notification":
                database.Context.NotificationQueue.Add(new NotificationQueueRow
                {
                    Id = Guid.NewGuid(),
                    UserId = missingUserId,
                    NotificationType = "job.updated",
                    PayloadJson = "{}",
                    Status = "Pending",
                    CreatedUtc = DateTimeOffset.UtcNow,
                    NextAttemptUtc = DateTimeOffset.UtcNow
                });
                break;
            case "job-view":
                database.Context.JobViews.Add(new JobViewRow
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    UserId = missingUserId,
                    ViewType = "details",
                    ViewedAt = DateTimeOffset.UtcNow
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dependentType), dependentType, null);
        }

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.Context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("job")]
    [InlineData("user")]
    public async Task Worksheet_rejects_cross_tenant_references(string crossTenantReference)
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var firstTenant = await SeedTenantAsync(database.Context);
        var secondTenant = await SeedTenantAsync(database.Context);

        database.Context.Worksheets.Add(new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = firstTenant.OrganizationId,
            JobId = crossTenantReference == "job" ? secondTenant.Id : firstTenant.Id,
            UserId = crossTenantReference == "user" ? secondTenant.UserId : firstTenant.UserId,
            WorkDate = DateTime.UtcNow.Date,
            HoursWorked = 1m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Job_rejects_cross_tenant_customer_reference()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var firstTenant = await SeedTenantAsync(database.Context);
        var secondTenant = await SeedTenantAsync(database.Context, includeCustomer: true);

        database.Context.JobReports.Add(new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = firstTenant.OrganizationId,
            CustomerId = secondTenant.CustomerId,
            ReportNumber = $"JOB-{Guid.NewGuid():N}",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Valid_same_tenant_references_continue_to_persist()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var tenant = await SeedTenantAsync(database.Context, includeCustomer: true);
        var now = DateTimeOffset.UtcNow;

        database.Context.Worksheets.Add(new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = tenant.OrganizationId,
            JobId = tenant.Id,
            UserId = tenant.UserId,
            WorkDate = now.UtcDateTime.Date,
            HoursWorked = 1m,
            CreatedAt = now,
            UpdatedAt = now
        });
        database.Context.PushSubscriptions.Add(new PushSubscriptionRow
        {
            Id = Guid.NewGuid(),
            UserId = tenant.UserId,
            Endpoint = "https://push.example.test/endpoint",
            P256Dh = "p256dh",
            Auth = "auth",
            CreatedUtc = now,
            LastSeenUtc = now
        });
        database.Context.NotificationQueue.Add(new NotificationQueueRow
        {
            Id = Guid.NewGuid(),
            UserId = tenant.UserId,
            NotificationType = "job.updated",
            PayloadJson = "{}",
            Status = "Pending",
            CreatedUtc = now,
            NextAttemptUtc = now
        });
        database.Context.JobViews.Add(new JobViewRow
        {
            Id = Guid.NewGuid(),
            JobId = tenant.Id,
            UserId = tenant.UserId,
            ViewType = "details",
            ViewedAt = now
        });

        await database.Context.SaveChangesAsync();

        Assert.Single(database.Context.Worksheets);
        Assert.Single(database.Context.PushSubscriptions);
        Assert.Single(database.Context.NotificationQueue);
        Assert.Single(database.Context.JobViews);
    }

    private static void AssertForeignKey<TDependent, TPrincipal>(
        SqlDbContext context,
        string[] dependentProperties,
        string[] principalProperties)
    {
        var entity = context.Model.FindEntityType(typeof(TDependent));
        Assert.NotNull(entity);

        var foreignKey = Assert.Single(
            entity!.GetForeignKeys(),
            candidate =>
                candidate.PrincipalEntityType.ClrType == typeof(TPrincipal)
                && candidate.Properties.Select(property => property.Name)
                    .SequenceEqual(dependentProperties));

        Assert.Equal(
            principalProperties,
            foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    private static SqlDbContext CreateModelContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private static async Task<SeededJob> SeedTenantAsync(
        SqlDbContext context,
        bool includeCustomer = false)
    {
        var now = DateTimeOffset.UtcNow;
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        Guid? customerId = includeCustomer ? Guid.NewGuid() : null;

        context.Organizations.Add(new OrganizationRow
        {
            Id = organizationId,
            Name = $"Tenant {organizationId:N}",
            Cvr = Interlocked.Increment(ref nextTestCvr).ToString(),
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Users.Add(new UserDataRow
        {
            Id = userId,
            OrganizationId = organizationId,
            Email = $"{userId:N}@example.test",
            DisplayName = "Test user",
            Role = "User",
            CreatedAt = now,
            UpdatedAt = now
        });

        if (customerId is not null)
        {
            context.Customers.Add(new CustomerRow
            {
                Id = customerId.Value,
                OrganizationId = organizationId,
                Name = "Test customer",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationId,
            CustomerId = customerId,
            ReportNumber = $"JOB-{jobId:N}",
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        });

        await context.SaveChangesAsync();
        return new SeededJob(jobId, organizationId, userId, customerId);
    }

    private sealed record SeededJob(
        Guid Id,
        Guid OrganizationId,
        Guid UserId,
        Guid? CustomerId);

    private sealed class RelationalTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RelationalTestDatabase(
            SqliteConnection connection,
            SqlDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        internal SqlDbContext Context { get; }

        internal static async Task<RelationalTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateFunction(
                "sysutcdatetime",
                () => DateTimeOffset.UtcNow.ToString("O"),
                isDeterministic: false);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
                .Options;
            var context = new SqlDbContext(options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");

            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
