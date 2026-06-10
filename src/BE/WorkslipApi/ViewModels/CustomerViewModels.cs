using Workslip.Application.Customers;

namespace Workslip.Api.ViewModels;

public sealed record CustomerListItemViewModel(
    Guid Id,
    string Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone,
    int JobCount);

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

public static class CustomerViewModelBuilder
{
    public static CustomerListItemViewModel ToListItem(CustomerListItemResponse customer) => new(
        customer.Id,
        customer.Name,
        customer.Address,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount);

    public static CustomerDetailViewModel ToDetail(CustomerDetailResponse customer) => new(
        customer.Id,
        customer.Name,
        customer.Address,
        customer.Email,
        customer.ContactPerson,
        customer.Phone,
        customer.JobCount,
        customer.Jobs.Select(ToJob).ToArray());

    private static CustomerJobViewModel ToJob(CustomerJobResponse job) => new(
        job.Id,
        job.ReportNumber,
        job.Status,
        job.UpdatedAt,
        job.ContactPerson,
        job.ContactPhone);
}
