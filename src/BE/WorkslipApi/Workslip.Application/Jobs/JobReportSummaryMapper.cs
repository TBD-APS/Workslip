using Workslip.Application.Auth;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

internal static class JobReportSummaryMapper
{
    public static JobReportSummaryResponse ToSummary(
        JobReportResponse report,
        ReferenceDataResponse referenceData,
        IReadOnlyList<WorksheetResponse> worksheets,
        ICurrentUserContext? user = null)
    {
        var isRegularUser = user != null && user.Role == Roles.User;
        var filteredWorksheets = isRegularUser
            ? worksheets.Where(w => w.UserId == user!.UserId).ToList()
            : worksheets;

        var closureFlags = report.ClosureFlags
            .Select(cf =>
            {
                var flagDefinition = referenceData.ClosureFlags.FirstOrDefault(x => x.Id == cf.Id);
                if (flagDefinition == null)
                    return null;

                return new JobReportSummaryClosureFlagResponse(
                    flagDefinition.Id,
                    flagDefinition.NormalizedLabel,
                    flagDefinition.Label);
            })
            .Where(x => x != null)
            .ToList();

        var totalHours = filteredWorksheets.Sum(w => w.HoursWorked);
        var totalOverLay = filteredWorksheets.Count(w => w.SleptOnJob);

        var customerSnapshot = new CustomerSnapshotResponse(
            report.Customer?.Name,
            report.Customer?.Email,
            report.Customer?.Phone,
            report.Customer?.Address,
            report.Customer?.ContactPerson);

        return new JobReportSummaryResponse(
            report.Id,
            report.OrganizationId,
            report.OrganizationName,
            report.OrganizationCvr,
            report.ReportNumber,
            report.Status,
            report.Customer?.CustomerId,
            customerSnapshot,
            report.DestinationAddress,
            report.DestinationZipCode,
            report.DestinationCity,
            report.JobType.ToString(),
            new JobReportSummaryWorkResponse(
                report.WorkKind,
                report.InstallationTypes,
                closureFlags!,
                report.Remarks),
            new JobReportSummaryObservationResponse(
                report.TaskDescription,
                report.CustomerObservations,
                report.TechnicalObservations),
            Array.Empty<ControlInstallationTypeResponse>(),
            report.Links,
            report.CreatedAt,
            report.UpdatedAt,
            report.SubmittedAt,
            report.AssignedUsers,
            filteredWorksheets,
            totalHours,
            totalOverLay,
            report.SoftDeleted,
            report.RejectionNote)
        {
            CreatedJobIds = report.CreatedJobIds
        };
    }
}
