namespace Workslip.Domain.Models;

public sealed class UserRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string DisplayName { get; init; } = String.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
