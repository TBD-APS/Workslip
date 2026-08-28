namespace Workslip.Domain.Models;

public sealed class OrganizationRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Cvr { get; init; } = "";
    public string? AccountingProviderId { get; init; }
    public string? EconomicsAgreementGrantTokenEncrypted { get; set; }
    public string? EconomicsAppSecretTokenEncrypted { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public ICollection<OrganizationFilialRow> Filials { get; set; } = [];
}
