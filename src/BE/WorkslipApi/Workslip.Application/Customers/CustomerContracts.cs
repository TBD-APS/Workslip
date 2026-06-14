namespace Workslip.Application.Customers;

public sealed record CustomerListItemResponse(
    Guid Id,
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone,
    int JobCount);

public sealed record CustomerJobResponse(
    Guid Id,
    string? ReportNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    string? ContactPerson,
    string? ContactPhone);

public sealed record CustomerDetailResponse(
    Guid Id,
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone,
    int JobCount,
    IReadOnlyList<CustomerJobResponse> Jobs);

public sealed record CustomerSearchResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ContactPerson);
