using System.Text.Json.Nodes;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.ViewModels;

public sealed record ControlCheckViewModel(
    Guid Id,
    string ItemId,
    bool Checked,
    string? Note);

public sealed record ControlSubcategoryViewModel(
    Guid Id,
    string InstallationTypeId,
    string SubcategoryId,
    IReadOnlyList<ControlCheckViewModel> ControlChecks);

public sealed record ControlInstallationTypeViewModel(
    string InstallationTypeId,
    IReadOnlyList<ControlSubcategoryViewModel> Subcategories);

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
    DateOnly? ReportDate,
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
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
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    IReadOnlyList<ControlInstallationTypeViewModel> ControlInstallationTypes,
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
    IReadOnlyList<ControlInstallationTypeViewModel> ControlInstallationTypes,
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
        job.ReportDate,
        job.InstallationTypes,
        job.WorkKind,
        job.CustomWorkKind,
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
        job.InstallationTypes,
        job.WorkKind,
        job.CustomWorkKind,
        job.Remarks,
        job.ClosureFlags,
        job.ControlInstallationTypes.Select(ToControlInstallationType).ToArray(),
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
        summary.ControlInstallationTypes.Select(ToControlInstallationType).ToArray(),
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

    private static ControlInstallationTypeViewModel ToControlInstallationType(ControlInstallationTypeResponse installationType) => new(
        installationType.InstallationTypeId,
        installationType.Subcategories.Select(ToControlSubcategory).ToArray());

    private static ControlSubcategoryViewModel ToControlSubcategory(ControlSubcategoryResponse subcategory) => new(
        subcategory.Id,
        subcategory.InstallationTypeId,
        subcategory.SubcategoryId,
        subcategory.ControlChecks.Select(ToControlCheck).ToArray());

    private static ControlCheckViewModel ToControlCheck(ControlCheckResponse check) => new(
        check.Id,
        check.ItemId,
        check.Checked,
        check.Note);
}
