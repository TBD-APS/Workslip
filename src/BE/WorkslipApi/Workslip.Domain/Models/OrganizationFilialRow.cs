using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Workslip.Domain.Models;

[Table("OrganizationFilials")]
public sealed class OrganizationFilialRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public OrganizationRow Organization { get; set; } = null!;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
