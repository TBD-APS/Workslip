namespace Workslip.Application.Customers;

public sealed record CustomerData(
    string? CustomerNumber,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record CustomerBulkCreateResult(
    int Imported,
    IReadOnlySet<string> ConflictingCustomerNumbers);

public sealed class CustomerNumberConflictException(IReadOnlySet<string> customerNumbers)
    : Exception("Et eller flere kundenumre findes allerede.")
{
    public IReadOnlySet<string> CustomerNumbers { get; } = customerNumbers;
}

public sealed record CustomerListItemResponse(
    Guid Id,
    string? CustomerNumber,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
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
    string? CustomerNumber,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
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
    string? CustomerNumber,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? ContactPerson,
    bool IsTop);

public sealed record UpdateCustomerRequest(
    string Name,
    string? CustomerNumber,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record CreateCustomerRequest(
    string Name,
    string? CustomerNumber,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record ImportCustomerRow(
    int RowNumber,
    string? CustomerNumber,
    string? Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record ImportCustomerError(int RowNumber, string Field, string Message);

public sealed record ImportCustomerResponse(
    int Imported,
    int Duplicates,
    int Skipped,
    int Failed,
    IReadOnlyList<ImportCustomerError> Errors);

public sealed record SetTopRequest(bool IsTop);
