namespace Workslip.Domain.Models;

public sealed class JobControlCheckRow
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public Guid SubcategoryDecisionId { get; init; }
    public string InstallationTypeId { get; init; } = string.Empty;
    public string SubcategoryId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public bool Checked { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
