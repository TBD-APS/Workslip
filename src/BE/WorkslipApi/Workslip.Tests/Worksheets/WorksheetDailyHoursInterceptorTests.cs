using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetDailyHoursInterceptorTests
{
    [Fact]
    public async Task SaveChanges_rejects_direct_write_that_pushes_user_day_above_24_hours()
    {
        await using var fixture = await Fixture.CreateAsync(existingHours: 20m);
        await using var context = fixture.CreateGuardedContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Worksheets.Add(fixture.CreateWorksheet(fixture.SecondJobId, 4.25m));

        await Assert.ThrowsAsync<WorksheetDailyHoursExceededException>(() => context.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task SaveChanges_allows_total_of_exactly_24_hours()
    {
        await using var fixture = await Fixture.CreateAsync(existingHours: 20m);
        await using var context = fixture.CreateGuardedContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Worksheets.Add(fixture.CreateWorksheet(fixture.SecondJobId, 4m));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        var total = await context.Worksheets
            .Where(row => row.OrganizationId == fixture.OrganizationId
                && row.UserId == fixture.UserId
                && row.WorkDate == fixture.WorkDate)
            .SumAsync(row => row.HoursWorked);
        Assert.Equal(24m, total);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SqlDbContext> _guardedOptions;

        private Fixture(
            SqliteConnection connection,
            DbContextOptions<SqlDbContext> guardedOptions,
            Guid organizationId,
            Guid userId,
            Guid secondJobId,
            DateTime workDate)
        {
            _connection = connection;
            _guardedOptions = guardedOptions;
            OrganizationId = organizationId;
            UserId = userId;
            SecondJobId = secondJobId;
            WorkDate = workDate;
        }

        public Guid OrganizationId { get; }
        public Guid UserId { get; }
        public Guid SecondJobId { get; }
        public DateTime WorkDate { get; }

        public static async Task<Fixture> CreateAsync(decimal existingHours)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var setupOptions = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
                .Options;
            var guardedOptions = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance, new WorksheetDailyHoursInterceptor())
                .Options;

            var organizationId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var firstJobId = Guid.NewGuid();
            var secondJobId = Guid.NewGuid();
            var workDate = new DateTime(2026, 8, 9);
            var now = DateTimeOffset.UtcNow;

            await using (var setup = new SqlDbContext(setupOptions))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Organizations.Add(new OrganizationRow
                {
                    Id = organizationId,
                    Name = "Org",
                    Cvr = "12345678"
                });
                setup.Users.Add(new UserDataRow
                {
                    Id = userId,
                    OrganizationId = organizationId,
                    Email = "worker@example.invalid",
                    DisplayName = "Worker",
                    EntraId = "entra-worker",
                    EntraEmail = "worker@example.invalid",
                    Role = Roles.User,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.JobReports.AddRange(
                    CreateJob(firstJobId, organizationId, "SAG-1", now),
                    CreateJob(secondJobId, organizationId, "SAG-2", now));
                setup.Worksheets.Add(new WorksheetRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    JobId = firstJobId,
                    UserId = userId,
                    WorkDate = workDate,
                    HoursWorked = existingHours,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await setup.SaveChangesAsync();
            }

            return new Fixture(connection, guardedOptions, organizationId, userId, secondJobId, workDate);
        }

        public SqlDbContext CreateGuardedContext() => new(_guardedOptions);

        public WorksheetRow CreateWorksheet(Guid jobId, decimal hours)
        {
            var now = DateTimeOffset.UtcNow;
            return new WorksheetRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                JobId = jobId,
                UserId = UserId,
                WorkDate = WorkDate,
                HoursWorked = hours,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        private static JobReportRow CreateJob(Guid id, Guid organizationId, string reportNumber, DateTimeOffset now) =>
            new()
            {
                Id = id,
                OrganizationId = organizationId,
                ReportNumber = reportNumber,
                Status = JobStatus.Draft.ToString(),
                JobType = JobType.Diverse,
                CreatedAt = now,
                UpdatedAt = now
            };
    }
}
