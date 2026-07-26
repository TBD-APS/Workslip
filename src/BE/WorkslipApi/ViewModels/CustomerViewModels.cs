using Workslip.Application.Customers;

namespace Workslip.Api.ViewModels;

public sealed record CustomerListItemViewModel(
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
    bool IsFavorite);

public sealed record CustomerJobViewModel(
    Guid Id,
    string? ReportNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    string? ContactPerson,
    string? ContactPhone);

public sealed record CustomerDetailViewModel(
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
    IReadOnlyList<CustomerJobViewModel> Jobs);

public sealed record CustomerListViewModel(
    IReadOnlyList<CustomerListItemViewModel> Items,
    int TotalCount);

public sealed record CustomerSearchViewModel(
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
    bool IsFavorite);

public sealed record CustomerImportErrorViewModel(int RowNumber, string Field, string Message);

public sealed record CustomerImportViewModel(
    int Imported,
    int Duplicates,
    int Skipped,
    int Failed,
    IReadOnlyList<CustomerImportErrorViewModel> Errors);

public static class CustomerViewModelBuilder
{
    public static CustomerListViewModel ToList(CustomerListResponse list) => new(
        list.Items.Select(ToListItem).ToArray(),
        list.TotalCount);

    public static CustomerListItemViewModel ToListItem(CustomerListItemResponse customer) => new(
        customer.Id,
        customer.CustomerNumber,
        customer.Name,
        customer.Address,
        customer.ZipCode,
        customer.City,
        customer.Country,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount,
        customer.IsFavorite);

    public static CustomerDetailViewModel ToDetail(CustomerDetailResponse customer) => new(
        customer.Id,
        customer.CustomerNumber,
        customer.Name,
        customer.Address,
        customer.ZipCode,
        customer.City,
        customer.Country,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount,
        customer.Jobs.Select(ToJob).ToArray());

    public static CustomerSearchViewModel ToSearch(CustomerSearchResponse customer) => new(
        customer.Id,
        customer.CustomerNumber,
        customer.Name,
        customer.Email,
        customer.Phone,
        customer.Address,
        customer.ZipCode,
        customer.City,
        customer.Country,
        customer.ContactPerson,
        customer.IsFavorite);

    private static CustomerJobViewModel ToJob(CustomerJobResponse job) => new(
        job.Id,
        job.ReportNumber,
        job.Status,
        job.UpdatedAt,
        job.ContactPerson,
        job.ContactPhone);
}
