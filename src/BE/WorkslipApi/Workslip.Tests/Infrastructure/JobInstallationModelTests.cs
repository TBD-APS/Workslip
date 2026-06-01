using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class JobInstallationModelTests
{
    [Fact]
    public void JobReportInstallations_have_unique_job_installation_definition_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationRow));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(JobReportInstallationRow.OrganizationId),
                nameof(JobReportInstallationRow.JobReportId),
                nameof(JobReportInstallationRow.InstallationTypeDefinitionId)
            ]));
    }

    [Fact]
    public void Selected_control_points_use_category_and_control_point_composite_key()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationControlPointRow));

        Assert.NotNull(entity);
        Assert.Equal([
            nameof(JobReportInstallationControlPointRow.JobReportInstallationCategoryId),
            nameof(JobReportInstallationControlPointRow.ControlPointId)
        ], entity!.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void JobReportInstallations_use_tenant_scoped_installation_definition_foreign_key()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationRow));

        Assert.NotNull(entity);
        var foreignKey = Assert.Single(entity!.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(InstallationTypeDefinitionRow));
        Assert.Equal([
            nameof(JobReportInstallationRow.OrganizationId),
            nameof(JobReportInstallationRow.InstallationTypeDefinitionId)
        ], foreignKey.Properties.Select(property => property.Name).ToArray());
        Assert.Equal([
            nameof(InstallationTypeDefinitionRow.OrganizationId),
            nameof(InstallationTypeDefinitionRow.Id)
        ], foreignKey.PrincipalKey.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void JobReportInstallationCategories_have_unique_installation_category_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationCategoryRow));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(JobReportInstallationCategoryRow.JobReportInstallationId),
                nameof(JobReportInstallationCategoryRow.ControlCategoryId)
            ]));
    }

    [Fact]
    public void Legacy_job_owned_installation_roots_are_not_active_model_roots()
    {
        using var context = CreateContext();

        Assert.Null(context.Model.FindEntityType(typeof(InstallationTypeRow)));
        Assert.Null(context.Model.FindEntityType(typeof(InstallationControlPointRow)));
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }
}
