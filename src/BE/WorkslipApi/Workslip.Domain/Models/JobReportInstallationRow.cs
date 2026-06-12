namespace Workslip.Domain.Models;

public sealed class JobReportInstallationRow : IJobRelated
{
    public Guid Id { get; set; }
    public Guid JobReportId { get; set; }
    public Guid OrganizationId { get; set; }
    public JobReportRow JobReport { get; set; } = null!;
    public Guid InstallationTypeDefinitionId { get; set; }
    public InstallationTypeDefinitionRow InstallationTypeDefinition { get; set; } = null!;
    public int SortOrder { get; set; }
    public ICollection<JobReportInstallationCategoryRow> Categories { get; set; } = [];
}
