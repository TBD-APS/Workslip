namespace Workslip.Domain.Models;

public sealed class JobReportClosureFlagRow : IJobRelated
{
    public Guid Id { get; init; }
    public Guid JobReportId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ClosureFlagId { get; init; }
    public int SortOrder { get; init; }

    public JobClosureFlagRow ClosureFlag { get; set; } = null!;
    public JobReportRow JobReport { get; set; } = null!;
}
