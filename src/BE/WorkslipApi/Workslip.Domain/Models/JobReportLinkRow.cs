namespace Workslip.Domain.Models;

public sealed class JobReportLinkRow
{
    public Guid Id { get; init; }
    public Guid SourceReportId { get; init; }
    public Guid TargetReportId { get; init; }
    public string LinkType { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
