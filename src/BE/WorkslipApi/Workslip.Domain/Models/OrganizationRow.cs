using System.ComponentModel.DataAnnotations;

namespace Workslip.Domain.Models;

public sealed class OrganizationRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Cvr { get; init; } = "";

    [MaxLength(80)]
    public string? AccountingProviderId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public ICollection<OrganizationFilialRow> Filials { get; set; } = [];
}
