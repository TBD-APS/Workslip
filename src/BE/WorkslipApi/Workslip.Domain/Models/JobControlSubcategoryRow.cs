namespace Workslip.Domain.Models;

public sealed class JobControlSubcategoryRow
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public string InstallationTypeId { get; init; } = string.Empty;
    public string SubcategoryId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
