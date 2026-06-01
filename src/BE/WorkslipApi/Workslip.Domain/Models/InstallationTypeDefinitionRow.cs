namespace Workslip.Domain.Models;

public sealed class InstallationTypeDefinitionRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public ICollection<InstallationTypeDefinitionMappingRow> Mappings { get; set; } = [];
    public ICollection<JobReportInstallationRow> JobReportInstallations { get; set; } = [];
}
