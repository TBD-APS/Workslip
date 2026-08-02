using Microsoft.EntityFrameworkCore;

namespace Workslip.Domain.Models;

[Index(nameof(Endpoint), IsUnique = true, Name = "UX_PushSubscriptions_Endpoint")]
public sealed class PushSubscriptionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256Dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
