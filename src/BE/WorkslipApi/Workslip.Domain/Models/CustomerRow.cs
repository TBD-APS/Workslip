namespace Workslip.Domain.Models;

public sealed class CustomerRow : IAuditable
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string? CustomerNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? ZipCode { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string? Email { get; init; }
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public bool IsFavorite { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
