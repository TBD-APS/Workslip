namespace Workslip.Domain.Models;

public sealed class JobReportInstallationCategoryRow : IAuditable
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid JobReportInstallationId { get; set; }
    public JobReportInstallationRow JobReportInstallation { get; set; } = null!;
    public Guid ControlCategoryId { get; set; }
    public ControlCategoryRow ControlCategory { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsIrrelevant { get; set; }
    public ICollection<JobReportInstallationControlPointRow> ControlPoints { get; set; } = [];
}
