namespace Workslip.Domain.Models;

public sealed class JobReportLinkRow : IJobRelated
{
    public Guid Id { get; init; }
    public Guid JobReportId => SourceReportId;
    public Guid OrganizationId { get; init; }
    public Guid SourceReportId { get; init; }
    public Guid TargetReportId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
