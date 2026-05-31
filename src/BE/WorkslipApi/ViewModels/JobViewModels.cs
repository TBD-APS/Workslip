using System.Text.Json.Nodes;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.ViewModels;

public sealed record InstallationTypeControlPointViewModel(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsRequired,
    bool isChecked);

public sealed record InstallationTypeCategoryViewModel(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<InstallationTypeControlPointViewModel> ControlPoints);

public sealed record InstallationTypeViewModel(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<InstallationTypeCategoryViewModel> Categories);

public sealed record CustomerViewModel(
    Guid? CustomerId,
    string? Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record WorksheetDayViewModel(
    DateOnly WorkDate,
    decimal HoursWorked);

public sealed record WorksheetUserGroupViewModel(
    string DisplayName,
    decimal TotalHours,
    IReadOnlyList<WorksheetDayViewModel> Entries);

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

public sealed record JobViewModel(
    Guid Id,
    Guid OrganizationId,
    CustomerViewModel? Customer,
    string? ReportNumber,
    JobStatus Status,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<InstallationTypeViewModel> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    IReadOnlyList<JobLinkInfoResponse> Links,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    IReadOnlyList<WorksheetUserGroupViewModel> Worksheets,
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
    bool SoftDeleted);

public sealed record JobLinkViewModel(
    Guid Id,
    Guid ReportId,
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedStatus,
    string LinkType);

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

    public static JobViewModel ToJob(JobReportResponse job) => new(
        job.Id,
        job.OrganizationId,
        ToCustomerViewModel(job.Customer),
        job.ReportNumber,
        job.Status,
        job.ReportDate,
        job.TaskDescription,
        job.CustomerObservations,
        job.TechnicalObservations,
        job.InstallationTypes.Select(ToInstallationType).ToArray(),
        job.WorkKind,
        job.CustomWorkKind,
        job.Remarks,
        job.ClosureFlags,
        job.Links,
        job.AssignedUsers,
        job.Worksheets.Select(ToWorksheetUserGroup).ToArray(),
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
        summary.SoftDeleted);

    public static JobLinkViewModel ToLink(JobLinkResponse link) => new(
        link.Id,
        link.ReportId,
        link.LinkedReportId,
        link.LinkedReportNumber,
        link.LinkedCustomerName,
        link.LinkedStatus,
        link.LinkType);

    private static WorksheetDayViewModel ToWorksheetDay(WorksheetDayEntry entry) => new(
        entry.WorkDate,
        entry.HoursWorked);

    private static WorksheetUserGroupViewModel ToWorksheetUserGroup(WorksheetUserGroupResponse group) => new(
        group.DisplayName,
        group.TotalHours,
        group.Entries.Select(ToWorksheetDay).ToArray());

    private static CustomerViewModel? ToCustomerViewModel(CustomerInfo? customer) =>
        customer is null ? null : new(
            customer.CustomerId,
            customer.Name,
            customer.Address,
            customer.Email,
            customer.ContactPerson,
            customer.Phone);

    private static InstallationTypeViewModel ToInstallationType(InstallationTypeResponse inst) => new(
        inst.Id,
        inst.Name,
        inst.Description,
        inst.SortOrder,
        inst.Categories.Select(ToCategory).ToArray());

    private static InstallationTypeCategoryViewModel ToCategory(InstallationTypeCategoryResponse cat) => new(
        cat.Id,
        cat.Name,
        cat.SortOrder,
        cat.ControlPoints.Select(ToControlPoint).ToArray());

    private static InstallationTypeControlPointViewModel ToControlPoint(InstallationTypeControlPointResponse cp) => new(
        cp.Id,
        cp.Name,
        cp.Description,
        cp.SortOrder,
        cp.IsRequired,
        cp.IsChecked);
}
