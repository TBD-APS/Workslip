using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetFinalizationGuardTests
{
    [Fact]
    public async Task Approved_job_rejects_worksheet_mutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new WorksheetFinalizationGuard())
            .Options;
        await using var context = new SqlDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var organizationId = Guid.NewGuid();
        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FilialId = Guid.NewGuid(),
            Email = "finalized@example.test",
            DisplayName = "Finalized User",
            EntraId = "finalized-user",
            EntraEmail = "finalized@example.test",
            Role = Roles.User,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var job = new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = JobStatus.Approved.ToString(),
            ReportNumber = "LOCKED",
            JobType = JobType.KLS,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var worksheet = new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobId = job.Id,
            UserId = user.Id,
            WorkDate = new DateTime(2026, 8, 14),
            HoursWorked = 2m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.Users.Add(user);
        context.JobReports.Add(job);
        context.Worksheets.Add(worksheet);
        await context.SaveChangesAsync();

        worksheet.HoursWorked = 3m;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Equal("Finalized worksheet history is immutable.", exception.Message);
    }
}
