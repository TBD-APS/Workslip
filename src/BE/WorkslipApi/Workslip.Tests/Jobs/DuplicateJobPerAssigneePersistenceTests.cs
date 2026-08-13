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

public sealed class DuplicateJobPerAssigneePersistenceTests
{
    [Fact]
    public async Task CreateAsync_creates_an_independent_job_per_assignee()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await fixture.SeedAsync(firstUserId, secondUserId);
        var linkedJobId = await fixture.SeedLinkedJobAsync();

        var request = new CreateJobRequest(
            CustomerSnapshot: new CustomerSnapshotData(
                "Kopi-kunde",
                "kunde@example.test",
                "12345678",
                "Testvej 1",
                "Test kontakt"),
            CreateCustomerFromSnapshot: true,
            Observations: new CreateJobObservationRequest(
                null,
                "Samme opgavebeskrivelse",
                null,
                null),
            JobType: JobType.KLS.ToString(),
            Timesheets:
            [
                new CreateTimesheetRequest("2026-08-13", firstUserId.ToString(), 1m, false),
                new CreateTimesheetRequest("2026-08-13", secondUserId.ToString(), 2m, false)
            ],
            AssignedUserIds: [firstUserId, secondUserId],
            DuplicatePerAssignedUser: true,
            LinkedJobIds: [linkedJobId]);

        var created = await fixture.Repository.CreateAsync(
            fixture.OrganizationId,
            request,
            request.AssignedUserIds!,
            fixture.AdminId,
            CancellationToken.None);

        var createdJobIds = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(created.CreatedJobIds);
        Assert.Equal(2, createdJobIds.Count);
        Assert.Equal(2, createdJobIds.Distinct().Count());

        var jobs = await fixture.Context.JobReports
            .AsNoTracking()
            .Where(job => createdJobIds.Contains(job.Id))
            .OrderBy(job => job.ReportNumber)
            .ToArrayAsync();
        Assert.Equal(2, jobs.Length);
        Assert.Equal(new string?[] { "0001", "0002" }, jobs.Select(job => job.ReportNumber));
        Assert.All(jobs, job =>
        {
            Assert.Equal("Samme opgavebeskrivelse", job.TaskDescription);
            Assert.Equal("Test kontakt", job.CustomerContactPerson);
            Assert.Equal(JobStatus.Draft.ToString(), job.Status);
        });
        Assert.Single(await fixture.Context.Customers.AsNoTracking().ToArrayAsync());
        Assert.Single(jobs.Select(job => job.CustomerId).Distinct());

        var assignments = await fixture.Context.JobAssignments
            .AsNoTracking()
            .Where(assignment => createdJobIds.Contains(assignment.ReportId))
            .OrderBy(assignment => assignment.ReportId)
            .ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.All(createdJobIds, jobId => Assert.Single(assignments, assignment => assignment.ReportId == jobId));
        Assert.Equal(
            new[] { firstUserId, secondUserId }.OrderBy(id => id),
            assignments.Select(assignment => assignment.UserId).OrderBy(id => id));

        var worksheets = await fixture.Context.Worksheets
            .AsNoTracking()
            .Where(worksheet => createdJobIds.Contains(worksheet.JobId))
            .ToArrayAsync();
        Assert.Equal(2, worksheets.Length);
        Assert.All(worksheets, worksheet =>
        {
            var assignment = Assert.Single(assignments, candidate => candidate.ReportId == worksheet.JobId);
            Assert.Equal(assignment.UserId, worksheet.UserId);
        });

        var links = await fixture.Context.JobReportLinks
            .AsNoTracking()
            .Where(link => createdJobIds.Contains(link.SourceReportId))
            .ToArrayAsync();
        Assert.Equal(2, links.Length);
        Assert.All(links, link => Assert.Equal(linkedJobId, link.TargetReportId));

        await fixture.Repository.TransitionAsync(
            createdJobIds[0],
            fixture.OrganizationId,
            JobStatus.InReview,
            firstUserId,
            null,
            CancellationToken.None);
        await fixture.Repository.TransitionAsync(
            createdJobIds[0],
            fixture.OrganizationId,
            JobStatus.Approved,
            fixture.AdminId,
            null,
            CancellationToken.None);

        var statuses = await fixture.Context.JobReports
            .AsNoTracking()
            .Where(job => createdJobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id, job => job.Status);
        Assert.Equal(JobStatus.Approved.ToString(), statuses[createdJobIds[0]]);
        Assert.Equal(JobStatus.Draft.ToString(), statuses[createdJobIds[1]]);
    }

    [Fact]
    public async Task CreateAsync_rolls_back_every_copy_and_new_customer_when_one_assignment_fails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var validUserId = Guid.NewGuid();
        await fixture.SeedAsync(validUserId);
        var missingUserId = Guid.NewGuid();
        var request = new CreateJobRequest(
            CustomerSnapshot: new CustomerSnapshotData("Rollback-kunde", null, null, null, null),
            CreateCustomerFromSnapshot: true,
            JobType: JobType.KLS.ToString(),
            AssignedUserIds: [validUserId, missingUserId],
            DuplicatePerAssignedUser: true);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Repository.CreateAsync(
            fixture.OrganizationId,
            request,
            request.AssignedUserIds!,
            fixture.AdminId,
            CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.JobReports.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fixture.Context.JobAssignments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fixture.Context.Customers.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task CreateAsync_rolls_back_every_copy_when_a_link_target_is_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await fixture.SeedAsync(firstUserId, secondUserId);
        var request = new CreateJobRequest(
            JobType: JobType.KLS.ToString(),
            AssignedUserIds: [firstUserId, secondUserId],
            DuplicatePerAssignedUser: true,
            LinkedJobIds: [Guid.NewGuid()]);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Repository.CreateAsync(
            fixture.OrganizationId,
            request,
            request.AssignedUserIds!,
            fixture.AdminId,
            CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.JobReports.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fixture.Context.JobAssignments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fixture.Context.JobReportLinks.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task CreateAsync_keeps_the_existing_shared_job_when_duplication_is_not_selected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await fixture.SeedAsync(firstUserId, secondUserId);
        var request = new CreateJobRequest(
            JobType: JobType.KLS.ToString(),
            AssignedUserIds: [firstUserId, secondUserId],
            DuplicatePerAssignedUser: false);

        var created = await fixture.Repository.CreateAsync(
            fixture.OrganizationId,
            request,
            request.AssignedUserIds!,
            fixture.AdminId,
            CancellationToken.None);

        Assert.Equal(new[] { created.Id }, created.CreatedJobIds);
        Assert.Single(await fixture.Context.JobReports.AsNoTracking().ToArrayAsync());
        var assignments = await fixture.Context.JobAssignments.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.All(assignments, assignment => Assert.Equal(created.Id, assignment.ReportId));
    }

    [Fact]
    public async Task ListAsync_scopes_a_regular_employee_to_their_own_duplicate_and_treats_missing_statuses_as_all()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await fixture.SeedAsync(firstUserId, secondUserId);
        var request = new CreateJobRequest(
            JobType: JobType.KLS.ToString(),
            AssignedUserIds: [firstUserId, secondUserId],
            DuplicatePerAssignedUser: true);

        var created = await fixture.Repository.CreateAsync(
            fixture.OrganizationId,
            request,
            request.AssignedUserIds!,
            fixture.AdminId,
            CancellationToken.None);

        var allJobs = await fixture.Repository.ListAsync(
            new JobQuery(
                fixture.OrganizationId,
                Statuses: null,
                Limit: 50,
                Offset: 0,
                CurrentUserId: fixture.AdminId),
            CancellationToken.None);
        var firstUsersJobs = await fixture.Repository.ListAsync(
            new JobQuery(
                fixture.OrganizationId,
                Statuses: null,
                Limit: 50,
                Offset: 0,
                CurrentUserId: firstUserId,
                AssignedToUserId: firstUserId),
            CancellationToken.None);

        Assert.Equal(2, allJobs.TotalCount);
        Assert.Equal(created.CreatedJobIds!.OrderBy(id => id), allJobs.Items.Select(job => job.Id).OrderBy(id => id));
        var assignedCopy = Assert.Single(firstUsersJobs.Items);
        Assert.Equal(1, firstUsersJobs.TotalCount);
        Assert.Equal(firstUserId, Assert.Single(assignedCopy.AssignedUsers).Id);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Fixture(SqliteConnection connection, SqlDbContext context, Guid organizationId, Guid adminId)
        {
            this.connection = connection;
            Context = context;
            OrganizationId = organizationId;
            AdminId = adminId;
            Repository = CreateRepository(
                context,
                new TestCurrentUserContext(adminId, organizationId, Roles.Admin));
        }

        internal SqlDbContext Context { get; }
        internal Guid OrganizationId { get; }
        internal Guid AdminId { get; }
        internal EfJobRepository Repository { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
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
            var context = new SqlDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new Fixture(connection, context, Guid.NewGuid(), Guid.NewGuid());
        }

        internal async Task SeedAsync(params Guid[] employeeIds)
        {
            var now = DateTimeOffset.UtcNow;
            Context.Organizations.Add(new OrganizationRow
            {
                Id = OrganizationId,
                Name = "Test organization",
                Cvr = "12345678",
                CreatedAt = now,
                UpdatedAt = now
            });
            Context.Users.Add(new UserDataRow
            {
                Id = AdminId,
                OrganizationId = OrganizationId,
                Email = "admin@example.test",
                DisplayName = "Test admin",
                Role = Roles.Admin,
                CreatedAt = now,
                UpdatedAt = now
            });
            Context.Users.AddRange(employeeIds.Select((employeeId, index) => new UserDataRow
            {
                Id = employeeId,
                OrganizationId = OrganizationId,
                Email = $"employee-{index + 1}@example.test",
                DisplayName = $"Employee {index + 1}",
                Role = Roles.User,
                CreatedAt = now,
                UpdatedAt = now
            }));
            await Context.SaveChangesAsync();
        }

        internal async Task<Guid> SeedLinkedJobAsync()
        {
            var linkedJobId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            Context.JobReports.Add(new JobReportRow
            {
                Id = linkedJobId,
                OrganizationId = OrganizationId,
                ReportNumber = "LINK",
                Status = JobStatus.Draft.ToString(),
                JobType = JobType.KLS,
                CreatedAt = now,
                UpdatedAt = now
            });
            await Context.SaveChangesAsync();
            return linkedJobId;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static EfJobRepository CreateRepository(SqlDbContext context, ICurrentUserContext currentUser)
        {
            var retryPolicy = new NoRetryPolicy();
            var worksheetRepository = new EfWorksheetRepository(context, currentUser, retryPolicy);
            var jobViewRepository = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
            var assignmentRepository = new EfAssignmentRepository(
                context,
                retryPolicy,
                currentUser,
                worksheetRepository,
                jobViewRepository);

            return new EfJobRepository(
                context,
                retryPolicy,
                new EfCustomerRepository(context, retryPolicy),
                assignmentRepository,
                new EfJobLinkRepository(context, retryPolicy),
                worksheetRepository,
                jobViewRepository);
        }
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
