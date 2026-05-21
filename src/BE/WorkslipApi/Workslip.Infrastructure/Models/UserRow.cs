namespace Workslip.Infrastructure.Models;

public sealed class UserRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string DisplayName { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string Role { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
