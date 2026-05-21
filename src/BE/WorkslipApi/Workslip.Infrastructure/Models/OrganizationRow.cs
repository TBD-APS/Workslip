namespace Workslip.Infrastructure.Models;

public sealed class OrganizationRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Cvr { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
