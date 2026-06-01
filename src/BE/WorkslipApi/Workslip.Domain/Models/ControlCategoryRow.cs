namespace Workslip.Domain.Models;

public sealed class ControlCategoryRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<JobReportInstallationCategoryRow> JobReportInstallationCategories { get; set; } = [];
}
