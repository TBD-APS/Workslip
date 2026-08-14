namespace Workslip.Domain.Models;

public sealed class InviteTokenRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string UserKind { get; set; } = UserKinds.Member;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? EntraUserId { get; set; }
    public string? EntraEmail { get; set; }
    public bool EntraCreatedByInvite { get; set; }
    public DateTimeOffset? EntraProvisionedAt { get; set; }
    public DateTimeOffset? EntraCleanedAt { get; set; }
}