namespace Workslip.Domain.Models;

public sealed class JobReportInstallationControlPointRow : IAuditable
{
    public Guid JobReportInstallationCategoryId { get; set; }
    public JobReportInstallationCategoryRow JobReportInstallationCategory { get; set; } = null!;
    public Guid ControlPointId { get; set; }
    public ControlPointRow ControlPoint { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsChecked { get; set; }
    public string? Comment { get; set; }
}
