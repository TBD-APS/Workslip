namespace Workslip.Domain.Models;

public sealed class UserDataRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FilialId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EntraId { get; set; } = string.Empty;
    public string EntraEmail { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}