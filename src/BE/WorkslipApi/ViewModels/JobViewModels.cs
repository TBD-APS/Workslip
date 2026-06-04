using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Api.ViewModels;

public sealed record CustomerViewModel(
    Guid? CustomerId,
    string? Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record JobListItemViewModel(
    Guid Id,
    Guid OrganizationId,
    CustomerViewModel? Customer,
    string? ReportNumber,
    JobStatus Status,
    IReadOnlyList<string> InstallationTypes,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    decimal? TotalHours);

public sealed record JobReportSummaryViewModel(
    Guid Id,
    Guid OrganizationId,
    string? ReportNumber,
    JobStatus Status,
    CustomerInfo Customer,
    JobReportSummaryWorkResponse Work,
    JobReportSummaryObservationResponse Observations,
    IReadOnlyList<JobLinkInfoResponse> Links,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    IReadOnlyList<WorksheetResponse> Worksheets,
    bool SoftDeleted);

public sealed record JobLinkViewModel(
    Guid Id,
    Guid ReportId,
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedStatus);

public static class JobViewModelBuilder
{
    public static JobListItemViewModel ToListItem(JobListItemResponse job) => new(
        job.Id,
        job.OrganizationId,
        ToCustomerViewModel(job.Customer),
        job.ReportNumber,
        job.Status,
        job.InstallationTypes,
        job.AssignedUsers,
        job.SoftDeleted,
        job.TotalHours);

    public static JobReportSummaryViewModel ToSummary(JobReportSummaryResponse summary) => new(
        summary.Id,
        summary.OrganizationId,
        summary.ReportNumber,
        summary.Status,
        summary.Customer,
        summary.Work,
        summary.Observations,
        summary.Links,
        summary.AssignedUsers,
        summary.Worksheets,
        summary.SoftDeleted);

    public static JobLinkViewModel ToLink(JobLinkResponse link) => new(
        link.Id,
        link.ReportId,
        link.LinkedReportId,
        link.LinkedReportNumber,
        link.LinkedCustomerName,
        link.LinkedStatus);

    public static List<JobLinkViewModel> ToLinkList(IReadOnlyList<JobLinkResponse> links) =>
        links.Select(ToLink).ToList();

    private static CustomerViewModel? ToCustomerViewModel(CustomerInfo? customer) =>
        customer is null ? null : new(
            customer.CustomerId,
            customer.Name,
            customer.Address,
            customer.Email,
            customer.ContactPerson,
            customer.Phone);
}
