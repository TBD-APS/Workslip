namespace Workslip.Domain.Models;

public sealed class ShopifyTenantMappingRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string ShopDomain { get; init; } = "";
    public string? AccessToken { get; init; }
    public string? WebhookSecret { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}