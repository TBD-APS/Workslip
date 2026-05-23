namespace Workslip.Domain.Models;

public sealed class InviteTokenRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? Role { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
