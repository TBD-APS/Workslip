namespace Workslip.Domain.Models;

public sealed class InstallationTypeDefinitionMappingRow
{
    public Guid InstallationTypeDefinitionId { get; set; }
    public InstallationTypeDefinitionRow InstallationTypeDefinition { get; set; } = null!;
    public Guid ControlCategoryId { get; set; }
    public ControlCategoryRow ControlCategory { get; set; } = null!;
    public Guid ControlPointId { get; set; }
    public ControlPointRow ControlPoint { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
}
