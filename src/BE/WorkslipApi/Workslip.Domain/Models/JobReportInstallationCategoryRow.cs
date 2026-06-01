namespace Workslip.Domain.Models;

public sealed class JobReportInstallationCategoryRow
{
    public Guid Id { get; set; }
    public Guid JobReportInstallationId { get; set; }
    public JobReportInstallationRow JobReportInstallation { get; set; } = null!;
    public Guid ControlCategoryId { get; set; }
    public ControlCategoryRow ControlCategory { get; set; } = null!;
    public int SortOrder { get; set; }
    public ICollection<JobReportInstallationControlPointRow> ControlPoints { get; set; } = [];
}
