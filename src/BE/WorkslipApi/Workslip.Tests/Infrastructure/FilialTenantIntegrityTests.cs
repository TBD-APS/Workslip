using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class FilialTenantIntegrityTests
{
    [Fact]
    public async Task Existing_single_filial_organization_auto_assigns_new_user_and_job()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization("Tenant A", "12345678");
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var filial = Assert.Single(await context.Set<OrganizationFilialRow>().ToListAsync());
        var now = DateTimeOffset.UtcNow;
        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Email = "user@example.invalid",
            DisplayName = "User",
            EntraId = "entra-user",
            EntraEmail = "user@example.invalid",
            Role = Roles.User,
            CreatedAt = now,
            UpdatedAt = now
        };
        var job = new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            ReportNumber = "0001",
            Status = JobStatus.Draft.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Users.Add(user);
        context.JobReports.Add(job);
        await context.SaveChangesAsync();

        Assert.Equal(filial.Id, user.FilialId);
        Assert.Equal(filial.Id, job.FilialId);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("job")]
    public async Task Explicit_filial_from_another_organization_is_rejected(string entityType)
    {
        await using var context = CreateContext();
        var first = CreateOrganization("Tenant A", "12345678");
        var second = CreateOrganization("Tenant B", "87654321");
        context.Organizations.AddRange(first, second);
        await context.SaveChangesAsync();

        var foreignFilialId = second.Id;
        var now = DateTimeOffset.UtcNow;
        if (entityType == "user")
        {
            context.Users.Add(new UserDataRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = first.Id,
                FilialId = foreignFilialId,
                Email = "cross-tenant@example.invalid",
                DisplayName = "Cross tenant",
                EntraId = "entra-cross-tenant",
                EntraEmail = "cross-tenant@example.invalid",
                Role = Roles.User,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            context.JobReports.Add(new JobReportRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = first.Id,
                FilialId = foreignFilialId,
                ReportNumber = "0001",
                Status = JobStatus.Draft.ToString(),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Installation_snapshot_children_inherit_parent_organization()
    {
        await using var context = CreateContext();
        var organizationId = Guid.NewGuid();
        var installation = new JobReportInstallationRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobReportId = Guid.NewGuid()
        };
        context.JobReportInstallations.Add(installation);
        await context.SaveChangesAsync();

        var category = new JobReportInstallationCategoryRow
        {
            Id = Guid.NewGuid(),
            JobReportInstallationId = installation.Id,
            ControlCategoryId = Guid.NewGuid(),
            SortOrder = 1
        };
        var point = new JobReportInstallationControlPointRow
        {
            JobReportInstallationCategoryId = category.Id,
            ControlPointId = Guid.NewGuid(),
            SortOrder = 1
        };
        context.JobReportInstallationCategories.Add(category);
        context.JobReportInstallationControlPoints.Add(point);

        await context.SaveChangesAsync();

        Assert.Equal(organizationId, category.OrganizationId);
        Assert.Equal(organizationId, point.OrganizationId);
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantIntegrityInterceptor())
            .Options;
        return new SqlDbContext(options);
    }

    private static OrganizationRow CreateOrganization(string name, string cvr)
    {
        var now = DateTimeOffset.UtcNow;
        return new OrganizationRow
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cvr = cvr,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
