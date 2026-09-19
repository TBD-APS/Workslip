namespace Workslip.Domain.Models;

public sealed class RefreshSessionRow
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? GraceReuseAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}
