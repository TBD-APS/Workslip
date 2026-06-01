namespace Workslip.Domain.Models;

public sealed class JobClosureFlagRow
{
    public Guid Id { get; set; }
    public string NormalizedLabel { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsExclusive { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
