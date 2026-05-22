namespace Workslip.Infrastructure.Models;

public sealed class JobClosureFlagRow
{
    public string Id { get; init; } = String.Empty;
    public string Label { get; init; } = String.Empty;
    public bool IsExclusive { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
