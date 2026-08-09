using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class NoControlPointsRelevantReasonMappingTests
{
    [Fact]
    public void Job_report_reason_uses_semantic_database_column()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new SqlDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(JobReportRow));
        var property = entityType?.FindProperty(nameof(JobReportRow.Remarks));
        var table = StoreObjectIdentifier.Table("JobReports", "dbo");

        Assert.NotNull(property);
        Assert.Equal("NoControlPointsRelevantReason", property!.GetColumnName(table));
    }
}
