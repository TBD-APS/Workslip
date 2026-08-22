using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobValidationServiceTests
{
    private readonly JobValidationService _service = new(NullLogger<JobValidationService>.Instance);

    [Fact]
    public void ValidateSubmitReady_accepts_complete_kls_job()
    {
        var result = _service.ValidateSubmitReady(CreateJob(), CreateReferenceData());

        Assert.Equal(ResultStatus.Ok, result.Status);
    }

    [Fact]
    public void ValidateSubmitReady_rejects_job_without_worksheet()
    {
        var result = _service.ValidateSubmitReady(CreateJob(hasWorksheet: false), CreateReferenceData());

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Identifier == nameof(JobReportResponse.Worksheets));
    }

    [Fact]
    public void ValidateSubmitReady_rejects_relevant_category_with_structured_control_point_path()
    {
        var result = _service.ValidateSubmitReady(CreateJob(controlPointChecked: false), CreateReferenceData());

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, error =>
            error.Identifier.StartsWith($"{nameof(JobReportResponse.InstallationTypes)}.", StringComparison.Ordinal)
            && error.Identifier.Contains(".Categories.", StringComparison.Ordinal)
            && error.Identifier.EndsWith(".ControlPoints", StringComparison.Ordinal)
            && error.ErrorMessage.Contains("Mindst et kontrolpunkt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSubmitReady_allows_irrelevant_category_without_selected_control_point()
    {
        var result = _service.ValidateSubmitReady(
            CreateJob(controlPointChecked: false, categoryIrrelevant: true),
            CreateReferenceData());

        Assert.Equal(ResultStatus.Ok, result.Status);
    }

    [Fact]
    public void ValidateSubmitReady_rejects_missing_closure_status()
    {
        var result = _service.ValidateSubmitReady(CreateJob(closureFlags: []), CreateReferenceData());

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Identifier == nameof(JobReportResponse.ClosureFlags));
    }

    [Fact]
    public void ValidateSubmitReady_rejects_operation_maintenance_as_only_closure_status()
    {
        var result = _service.ValidateSubmitReady(
            CreateJob(closureFlags: [CreateClosureFlag(ClosureFlagLabels.OperationMaintenanceInstructions)]),
            CreateReferenceData());

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, error =>
            error.Identifier == nameof(JobReportResponse.ClosureFlags)
            && error.ErrorMessage.Contains("Vælg også", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSubmitReady_keeps_diverse_jobs_outside_kls_completion_rules()
    {
        var result = _service.ValidateSubmitReady(
            CreateJob(
                jobType: JobType.Diverse,
                hasWorksheet: false,
                controlPointChecked: false,
                closureFlags: []),
            CreateReferenceData());

        Assert.Equal(ResultStatus.Ok, result.Status);
    }

    private static ReferenceDataResponse CreateReferenceData() =>
        new(
            Array.Empty<InstallationTypeDefinitionResponse>(),
            [new WorkKindResponse(Guid.NewGuid(), "Installation", "Installation", false, 1)],
            Array.Empty<ClosureFlagResponse>());

    private static JobReportResponse CreateJob(
        JobType jobType = JobType.KLS,
        bool hasWorksheet = true,
        bool controlPointChecked = true,
        bool categoryIrrelevant = false,
        IReadOnlyList<ClosureFlagResponse>? closureFlags = null)
    {
        var now = DateTimeOffset.UtcNow;
        var installationType = new InstallationTypeResponse(
            Guid.NewGuid(),
            "Vand",
            1,
            [
                new InstallationTypeCategoryResponse(
                    Guid.NewGuid(),
                    "installation",
                    1,
                    [
                        new InstallationTypeControlPointResponse(
                            Guid.NewGuid(),
                            "Kontrollér installation",
                            1,
                            true,
                            controlPointChecked)
                    ],
                    categoryIrrelevant)
            ]);

        return new JobReportResponse(
            Id: Guid.NewGuid(),
            OrganizationId: Guid.NewGuid(),
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            Customer: new CustomerInfo(Guid.NewGuid(), "Testkunde", null, null, null, null),
            ReportNumber: "0001",
            DestinationAddress: null,
            DestinationZipCode: null,
            DestinationCity: null,
            Status: JobStatus.Draft,
            ReportDate: null,
            JobType: jobType,
            TaskDescription: null,
            CustomerObservations: null,
            TechnicalObservations: null,
            InstallationTypes: [installationType],
            WorkKind: new JobWorkKindResponse(Guid.NewGuid(), "Installation", "Installation", false, 1, null),
            Remarks: null,
            ClosureFlags: closureFlags ?? [CreateClosureFlag(ClosureFlagLabels.Completed)],
            Links: Array.Empty<JobLinkInfoResponse>(),
            CreatedAt: now,
            UpdatedAt: now,
            SubmittedAt: null,
            AssignedUsers: Array.Empty<AssignedUserResponse>(),
            Worksheets: hasWorksheet
                ? [new WorksheetUserGroupResponse("Montør", 1m, [new WorksheetDayEntry(DateOnly.FromDateTime(DateTime.UtcNow), 1m)])]
                : Array.Empty<WorksheetUserGroupResponse>(),
            SoftDeleted: false,
            DeletionScheduledAt: null,
            TotalHours: hasWorksheet ? 1m : null,
            RejectionNote: null);
    }

    private static ClosureFlagResponse CreateClosureFlag(string normalizedLabel) =>
        new(Guid.NewGuid(), normalizedLabel, normalizedLabel, false, 1);
}
