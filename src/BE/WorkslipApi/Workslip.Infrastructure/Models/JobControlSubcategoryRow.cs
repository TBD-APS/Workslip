namespace Workslip.Infrastructure.Models;

public sealed class JobControlSubcategoryRow
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public string CategoryId { get; init; } = string.Empty;
    public string SubcategoryId { get; init; } = string.Empty;
    public bool IsIrrelevant { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
