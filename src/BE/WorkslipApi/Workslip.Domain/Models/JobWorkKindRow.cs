namespace Workslip.Domain.Models;

public sealed class JobWorkKindRow
{
    public Guid Id { get; set; }
    public string NormalizedLabel { get; init; } = String.Empty;
    public string Label { get; init; } = String.Empty;
    public bool RequiresCustomWorkKind { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}
