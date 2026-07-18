using Workslip.Application.Customers;

namespace Workslip.Api.ViewModels;

public sealed record CustomerListItemViewModel(
    Guid Id,
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone,
    int JobCount,
    bool IsTop);

public sealed record CustomerJobViewModel(
    Guid Id,
    string? ReportNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    string? ContactPerson,
    string? ContactPhone);

public sealed record CustomerDetailViewModel(
    Guid Id,
    string Name,
    string? Address,
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
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ContactPerson,
    bool IsTop);

public static class CustomerViewModelBuilder
{
    public static CustomerListViewModel ToList(CustomerListResponse list) => new(
        list.Items.Select(ToListItem).ToArray(),
        list.TotalCount);

    public static CustomerListItemViewModel ToListItem(CustomerListItemResponse customer) => new(
        customer.Id,
        customer.Name,
        customer.Address,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount,
        customer.IsTop);

    public static CustomerDetailViewModel ToDetail(CustomerDetailResponse customer) => new(
        customer.Id,
        customer.Name,
        customer.Address,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount,
        customer.Jobs.Select(ToJob).ToArray());

    public static CustomerSearchViewModel ToSearch(CustomerSearchResponse customer) => new(
        customer.Id,
        customer.Name,
        customer.Email,
        customer.Phone,
        customer.Address,
        customer.ContactPerson,
        customer.IsTop);

    private static CustomerJobViewModel ToJob(CustomerJobResponse job) => new(
        job.Id,
        job.ReportNumber,
        job.Status,
        job.UpdatedAt,
        job.ContactPerson,
        job.ContactPhone);
}
