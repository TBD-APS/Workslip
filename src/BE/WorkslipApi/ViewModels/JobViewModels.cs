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

public sealed record JobListItemViewModel(
    Guid Id,
    Guid OrganizationId,
    Guid? CustomerId,
    string? ReportNumber,
    JobStatus Status,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    DateOnly? ReportDate,
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    AssignedUserResponse? AssignedUser,
    bool SoftDeleted);

public sealed record JobViewModel(
    Guid Id,
    Guid OrganizationId,
    Guid? CustomerId,
    string? ReportNumber,
    JobStatus Status,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlInstallationTypeViewModel> ControlInstallationTypes,
    IReadOnlyList<JobLinkInfoResponse> Links,
    AssignedUserResponse? AssignedUser,
    bool SoftDeleted);

public sealed record JobReportSummaryViewModel(
    Guid Id,
    Guid OrganizationId,
    string? ReportNumber,
    JobStatus Status,
    JobReportSummaryCustomerResponse Customer,
    JobReportSummaryWorkResponse Work,
    JobReportSummaryObservationResponse Observations,
    IReadOnlyList<ControlInstallationTypeViewModel> ControlInstallationTypes,
    IReadOnlyList<JobLinkInfoResponse> Links,
    AssignedUserResponse? AssignedUser,
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
        job.CustomerId,
        job.ReportNumber,
        job.Status,
        job.CustomerName,
        job.CustomerAddress,
        job.CustomerEmail,
        job.ReportDate,
        job.InstallationTypes,
        job.WorkKind,
        job.CustomWorkKind,
        job.AssignedUser,
        job.SoftDeleted);

    public static JobViewModel ToJob(JobReportResponse job) => new(
        job.Id,
        job.OrganizationId,
        job.CustomerId,
        job.ReportNumber,
        job.Status,
        job.CustomerName,
        job.CustomerAddress,
        job.CustomerEmail,
        job.ContactPerson,
        job.Phone,
        job.ReportDate,
        job.TaskDescription,
        job.CustomerObservations,
        job.TechnicalObservations,
        job.InstallationTypes,
        job.WorkKind,
        job.CustomWorkKind,
        job.Remarks,
        job.ClosureFlags,
        job.Payload,
        job.ControlInstallationTypes.Select(ToControlInstallationType).ToArray(),
        job.Links,
        job.AssignedUser,
        job.SoftDeleted);

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
        summary.AssignedUser,
        summary.SoftDeleted);

    public static JobLinkViewModel ToLink(JobLinkResponse link) => new(
        link.Id,
        link.ReportId,
        link.LinkedReportId,
        link.LinkedReportNumber,
        link.LinkedCustomerName,
        link.LinkedStatus,
        link.LinkType);

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
