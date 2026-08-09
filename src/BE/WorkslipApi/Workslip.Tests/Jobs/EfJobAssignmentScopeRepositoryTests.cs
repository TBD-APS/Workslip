using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;

namespace Workslip.Tests.Jobs;

public sealed class EfJobAssignmentScopeRepositoryTests
{
    [Fact]
    public async Task Scope_queries_preserve_organization_and_filial_ownership()
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
        var filialA1 = Guid.NewGuid();
        var filialA2 = Guid.NewGuid();
        var filialB = Guid.NewGuid();
        var employeeA1 = Guid.NewGuid();
        var employeeA2 = Guid.NewGuid();
        var foreignEmployee = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        context.Organizations.AddRange(
            CreateOrganization(organizationA, "Organization A", "12345678", now),
            CreateOrganization(organizationB, "Organization B", "87654321", now));
        context.Set<OrganizationFilialRow>().AddRange(
            CreateFilial(filialA1, organizationA, "A1", isDefault: true, now),
            CreateFilial(filialA2, organizationA, "A2", isDefault: false, now),
            CreateFilial(filialB, organizationB, "B1", isDefault: true, now));
        await context.SaveChangesAsync();

        context.Users.AddRange(
            CreateUser(employeeA1, organizationA, filialA1, Roles.User, "a1@example.invalid", now),
            CreateUser(employeeA2, organizationA, filialA2, Roles.User, "a2@example.invalid", now),
            CreateUser(foreignEmployee, organizationB, filialB, Roles.User, "b1@example.invalid", now));
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationA,
            FilialId = filialA2,
            ReportNumber = "0001",
            Status = JobStatus.Draft.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        var repository = new EfJobAssignmentScopeRepository(context);

        var defaultFilialId = await repository.GetDefaultFilialIdAsync(organizationA, CancellationToken.None);
        var jobFilialId = await repository.GetJobFilialIdAsync(organizationA, jobId, CancellationToken.None);
        var users = await repository.GetUserScopesAsync(
            organizationA,
            [employeeA1, employeeA2, foreignEmployee],
            CancellationToken.None);

        Assert.Equal(filialA1, defaultFilialId);
        Assert.Equal(filialA2, jobFilialId);
        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Id == employeeA1 && user.FilialId == filialA1 && user.Role == Roles.User);
        Assert.Contains(users, user => user.Id == employeeA2 && user.FilialId == filialA2 && user.Role == Roles.User);
        Assert.DoesNotContain(users, user => user.Id == foreignEmployee);
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
        bool isDefault,
        DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Name = name,
        IsDefault = isDefault,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static UserDataRow CreateUser(
        Guid id,
        Guid organizationId,
        Guid filialId,
        string role,
        string email,
        DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        FilialId = filialId,
        Email = email,
        DisplayName = email,
        Phone = "+4512345678",
        EntraId = $"entra-{id:N}",
        EntraEmail = email,
        Role = role,
        CreatedAt = now,
        UpdatedAt = now
    };
}
