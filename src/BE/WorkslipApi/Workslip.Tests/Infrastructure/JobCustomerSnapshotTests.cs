using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Application.Auth;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class JobCustomerSnapshotTests
{
    [Fact]
    public async Task CreateAsync_with_new_customer_creates_master_and_sets_fk()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        await context.SaveChangesAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var snapshot = new CustomerSnapshotData(
            Name: "New Customer",
            Email: "new@test.com",
            Phone: "12345678",
            Address: "Main Street 1",
            ContactPerson: null);

        var request = new CreateJobRequest(
            CustomerSnapshot: snapshot,
            CreateCustomerFromSnapshot: true,
            Work: null,
            Observations: null);

        await repo.CreateAsync(orgId, request, [], actorId, CancellationToken.None);

        var masterCustomer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Name == "New Customer");
        Assert.NotNull(masterCustomer);
        Assert.Equal("Main Street 1", masterCustomer.Address);
        Assert.Equal("new@test.com", masterCustomer.Email);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.CustomerName == "New Customer");
        Assert.NotNull(job);
        Assert.Equal(masterCustomer.Id, job.CustomerId);
        // Snapshot should reflect the customer data
        Assert.Equal("New Customer", job.CustomerName);
        Assert.Equal("new@test.com", job.CustomerEmail);
        Assert.Equal("12345678", job.CustomerPhone);
        Assert.Equal("Main Street 1", job.CustomerAddress);
    }

    [Fact]
    public async Task CreateAsync_with_existing_customerId_uses_fk_and_never_creates_new_master()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        var existingCustomer = new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Existing Customer",
            Address = "Old Address",
            Email = "existing@test.com",
            ContactPerson = "Contact",
            Phone = "87654321",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.Customers.Add(existingCustomer);
        await context.SaveChangesAsync();

        var beforeCount = await context.Customers.CountAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var snapshot = new CustomerSnapshotData(
            Name: "Existing Customer",
            Email: "existing@test.com",
            Phone: "87654321",
            Address: "Old Address",
            ContactPerson: "Contact");

        var request = new CreateJobRequest(
            CustomerId: existingCustomer.Id,
            CustomerSnapshot: snapshot);

        await repo.CreateAsync(orgId, request, [], actorId, CancellationToken.None);

        var afterCount = await context.Customers.CountAsync();
        Assert.Equal(beforeCount, afterCount);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.CustomerId == existingCustomer.Id);
        Assert.NotNull(job);
        Assert.Equal(existingCustomer.Id, job.CustomerId);
    }

    [Fact]
    public async Task CreateAsync_with_snapshot_only_sets_snapshot_columns_without_fk()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        await context.SaveChangesAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var snapshot = new CustomerSnapshotData(
            Name: "Snapshot Name",
            Email: "snapshot@test.com",
            Phone: "11223344",
            Address: "Snapshot Address",
            ContactPerson: null);

        var request = new CreateJobRequest(
            CustomerSnapshot: snapshot);

        await repo.CreateAsync(orgId, request, [], actorId, CancellationToken.None);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.CustomerName == "Snapshot Name");
        Assert.NotNull(job);
        Assert.Null(job.CustomerId);
        Assert.Equal("Snapshot Name", job.CustomerName);
        Assert.Equal("snapshot@test.com", job.CustomerEmail);
        Assert.Equal("11223344", job.CustomerPhone);
        Assert.Equal("Snapshot Address", job.CustomerAddress);

        // No customer should have been created in master data
        Assert.Empty(context.Customers.AsNoTracking());
    }

    [Fact]
    public async Task CreateAsync_with_CustomerSnapshot_overrides_Customer_values()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        await context.SaveChangesAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var snapshot = new CustomerSnapshotData(
            Name: "Snapshot Override",
            Email: null,
            Phone: null,
            Address: null,
            ContactPerson: null);

        var request = new CreateJobRequest(
            CustomerSnapshot: snapshot,
            CreateCustomerFromSnapshot: true);

        await repo.CreateAsync(orgId, request, [], actorId, CancellationToken.None);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.CustomerName == "Snapshot Override");
        Assert.NotNull(job);
        Assert.Equal("Snapshot Override", job.CustomerName);
        // Snapshot's null fields should NOT fall through to master values
        Assert.Null(job.CustomerEmail);
        Assert.Null(job.CustomerPhone);
        Assert.Null(job.CustomerAddress);

        // The created master uses the same snapshot values, including explicit nulls.
        var master = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Name == "Snapshot Override");
        Assert.NotNull(master);
        Assert.Null(master.Email);
        Assert.Null(master.Phone);
        Assert.Null(master.Address);
    }

    [Fact]
    public async Task UpdateAsync_with_Customer_changes_fk_and_updates_snapshot_without_touching_master()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        var firstCustomer = new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "First Customer",
            Email = "first@test.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var secondCustomer = new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Second Customer",
            Email = "second@test.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.Customers.AddRange(firstCustomer, secondCustomer);
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            CustomerId = firstCustomer.Id,
            CustomerName = "First Customer",
            CustomerEmail = "first@test.com",
            ReportNumber = "JOB-UPD-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var updateSnapshot = new CustomerSnapshotData(
            Name: "Second Customer",
            Email: "second@test.com",
            Phone: null,
            Address: null,
            ContactPerson: null);

        var updateRequest = new UpdateJobRequest(
            CustomerSnapshot: updateSnapshot);

        await repo.UpdateAsync(jobId, orgId, updateRequest, CancellationToken.None);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == jobId);
        Assert.NotNull(job);
        Assert.Equal("Second Customer", job.CustomerName);

        // Master customer records should be completely untouched
        var first = await context.Customers.AsNoTracking().FirstAsync(c => c.Id == firstCustomer.Id);
        Assert.Equal("First Customer", first.Name);
        Assert.Equal("first@test.com", first.Email);

        var second = await context.Customers.AsNoTracking().FirstAsync(c => c.Id == secondCustomer.Id);
        Assert.Equal("Second Customer", second.Name);
        Assert.Equal("second@test.com", second.Email);
    }

    [Fact]
    public async Task UpdateAsync_with_CustomerSnapshot_only_updates_snapshot_columns_and_preserves_fk()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedOrg(context, orgId);
        context.Customers.Add(new CustomerRow
        {
            Id = customerId,
            OrganizationId = orgId,
            Name = "Original Master",
            Email = "original@test.com",
            Address = "Original Address",
            Phone = "11111111",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            CustomerId = customerId,
            CustomerName = "Original Master",
            CustomerEmail = "original@test.com",
            CustomerPhone = "11111111",
            CustomerAddress = "Original Address",
            ReportNumber = "JOB-SNP-UPD",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var repo = CreateJobRepository(context, actorId, orgId);
        var snapshot = new CustomerSnapshotData(
            Name: "Snapshot Only Override",
            Email: null,
            Phone: "99999999",
            Address: null,
            ContactPerson: null);

        var updateRequest = new UpdateJobRequest(
            CustomerSnapshot: snapshot);

        await repo.UpdateAsync(jobId, orgId, updateRequest, CancellationToken.None);

        var job = await context.JobReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == jobId);
        Assert.NotNull(job);
        Assert.Equal(customerId, job.CustomerId);
        Assert.Equal("Snapshot Only Override", job.CustomerName);
        Assert.Equal("99999999", job.CustomerPhone);
        Assert.Null(job.CustomerEmail);
        Assert.Null(job.CustomerAddress);

        // Master customer completely untouched
        var master = await context.Customers.AsNoTracking().FirstAsync(c => c.Id == customerId);
        Assert.Equal("Original Master", master.Name);
        Assert.Equal("original@test.com", master.Email);
        Assert.Equal("Original Address", master.Address);
        Assert.Equal("11111111", master.Phone);
    }

    private static EfJobRepository CreateJobRepository(SqlDbContext context, Guid userId, Guid organizationId)
    {
        var retryPolicy = new NoRetryPolicy();
        var currentUser = new TestCurrentUserContext(userId, organizationId);
        var worksheetRepository = new EfWorksheetRepository(context, currentUser, retryPolicy);
        var jobViewRepository = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
        var assignmentRepository = new EfAssignmentRepository(context, retryPolicy, currentUser, worksheetRepository, jobViewRepository);
        var linkRepository = new EfJobLinkRepository(context, retryPolicy);
        return new EfJobRepository(
            context,
            retryPolicy,
            new EfCustomerRepository(context, retryPolicy),
            assignmentRepository,
            linkRepository,
            worksheetRepository,
            jobViewRepository);
    }

    private static SqlDbContext CreateContext(Guid organizationId, Guid userId)
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new AuditInterceptor(new TestCurrentUserContext(userId, organizationId)))
            .Options;

        return new SqlDbContext(options);
    }

    private static void SeedOrg(SqlDbContext context, Guid orgId)
    {
        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "TestOrg", Cvr = "12345678" });
        context.IsSeeding = false;
    }

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => "Admin";
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
