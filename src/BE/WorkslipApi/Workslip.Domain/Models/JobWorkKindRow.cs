namespace Workslip.Domain.Models;

public sealed class JobWorkKindRow
{
    public string Id { get; init; } = String.Empty;
    public string Label { get; init; } = String.Empty;
    public bool RequiresCustomWorkKind { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
