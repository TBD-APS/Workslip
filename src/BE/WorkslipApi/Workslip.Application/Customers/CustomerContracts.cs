namespace Workslip.Application.Customers;

public sealed record CustomerListItemResponse(
    Guid Id,
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone,
    int JobCount,
    bool IsTop);

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

public sealed record CustomerListResponse(
    IReadOnlyList<CustomerListItemResponse> Items,
    int TotalCount);

public sealed record CustomerSearchResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ContactPerson,
    bool IsTop);

public sealed record UpdateCustomerRequest(
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record CreateCustomerRequest(
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record DeleteCustomerResponse(bool success);

public sealed record ImportCustomerResponse(int Imported, int Skipped);

public sealed record SetTopRequest(bool IsTop);
