namespace Workslip.Domain.Models;

public sealed class ControlPointRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public ICollection<JobReportInstallationControlPointRow> JobReportInstallationControlPoints { get; set; } = [];
}
