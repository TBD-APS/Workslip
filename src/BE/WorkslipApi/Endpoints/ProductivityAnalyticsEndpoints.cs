using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints;

public static class ProductivityAnalyticsEndpoints
{
    private const string CaseCreationDurationEventType = "analytics.case_create_duration";

    public static IEndpointRouteBuilder MapProductivityAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var userGroup = app.MapUserGroup("/api/productivity", "productivity");
        userGroup.MapPost("/case-create-duration", RecordCaseCreationDurationAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        var superAdminGroup = app.MapSuperAdminGroup("/api/superadmin/analytics", "superadmin-analytics");
        superAdminGroup.MapGet("/case-flow", GetCaseFlowAnalyticsAsync)
            .Produces<SuperAdminCaseFlowAnalyticsResponse>();

        return app;
    }

    private static async Task<IResult> RecordCaseCreationDurationAsync(
        CaseCreationDurationRequest request,
        ICurrentUserContext currentUser,
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId
            || currentUser.UserId is not Guid userId)
        {
            return Results.Forbid();
        }

        if (!string.Equals(currentUser.Role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        var jobIds = request.JobIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (jobIds.Length == 0 || request.DurationSeconds is < 1 or > 86_400)
        {
            return Results.BadRequest(new { error = "invalid_case_creation_metric" });
        }

        var matchingJobCount = await dbContext.JobReports
            .AsNoTracking()
            .CountAsync(job =>
                job.OrganizationId == organizationId
                && jobIds.Contains(job.Id),
                cancellationToken);

        if (matchingJobCount != jobIds.Length)
        {
            return Results.BadRequest(new { error = "case_creation_metric_job_mismatch" });
        }

        dbContext.JobEvents.Add(new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ReportId = null,
            ActorId = string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
                ? null
                : userId,
            EventType = CaseCreationDurationEventType,
            Summary = $"Sag oprettet via Workslip på {request.DurationSeconds} sek.",
            BeforeJson = null,
            AfterJson = JsonSerializer.Serialize(new
            {
                jobIds,
                durationSeconds = request.DurationSeconds,
                generatedCaseCount = jobIds.Length
            }),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCaseFlowAnalyticsAsync(
        int? days,
        Guid? organizationId,
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var windowDays = Math.Clamp(days ?? 90, 7, 365);
        var generatedAt = DateTimeOffset.UtcNow;
        var windowStart = generatedAt.AddDays(-windowDays);

        var organizationsQuery = dbContext.Organizations.AsNoTracking();
        if (organizationId.HasValue)
        {
            organizationsQuery = organizationsQuery.Where(org => org.Id == organizationId.Value);
        }

        var organizations = await organizationsQuery
            .Select(org => new OrganizationProjection(org.Id, org.Name))
            .OrderBy(org => org.Name)
            .ToListAsync(cancellationToken);

        if (organizationId.HasValue && organizations.Count == 0)
        {
            return Results.NotFound(new { error = "organization_not_found" });
        }

        var allowedOrganizationIds = organizations.Select(org => org.Id).ToHashSet();

        var jobsQuery = dbContext.JobReports
            .AsNoTracking()
            .Where(job => !job.IsSoftDeleted && job.CreatedAt >= windowStart);
        if (organizationId.HasValue)
        {
            jobsQuery = jobsQuery.Where(job => job.OrganizationId == organizationId.Value);
        }

        var jobs = await jobsQuery
            .Select(job => new JobProjection(
                job.Id,
                job.OrganizationId,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt,
                job.SubmittedAt,
                job.SubmittedByUserId))
            .ToListAsync(cancellationToken);

        var assignments = await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join job in dbContext.JobReports.AsNoTracking() on assignment.ReportId equals job.Id
            where !job.IsSoftDeleted && job.CreatedAt >= windowStart
            where !organizationId.HasValue || job.OrganizationId == organizationId.Value
            select new AssignmentProjection(assignment.ReportId, assignment.UserId)
        ).ToListAsync(cancellationToken);

        var views = await (
            from view in dbContext.JobViews.AsNoTracking()
            join job in dbContext.JobReports.AsNoTracking() on view.JobId equals job.Id
            where !job.IsSoftDeleted && job.CreatedAt >= windowStart
            where !organizationId.HasValue || job.OrganizationId == organizationId.Value
            where view.ViewType == JobViewTypes.New
            select new ViewProjection(view.JobId, view.UserId, view.ViewedAt)
        ).ToListAsync(cancellationToken);

        var jobIdSet = jobs.Select(job => job.Id).ToHashSet();
        var lifecycleEventsQuery = dbContext.JobEvents
            .AsNoTracking()
            .Where(evt => evt.ReportId != null && evt.CreatedAt >= windowStart && evt.AfterJson != null);
        if (organizationId.HasValue)
        {
            lifecycleEventsQuery = lifecycleEventsQuery.Where(evt => evt.OrganizationId == organizationId.Value);
        }

        var lifecycleEvents = (await lifecycleEventsQuery
            .Select(evt => new LifecycleEventProjection(
                evt.OrganizationId,
                evt.ReportId,
                evt.AfterJson!,
                evt.CreatedAt))
            .ToListAsync(cancellationToken))
            .Where(evt => evt.ReportId.HasValue && jobIdSet.Contains(evt.ReportId.Value))
            .ToList();

        var creationEventsQuery = dbContext.JobEvents
            .AsNoTracking()
            .Where(evt => evt.EventType == CaseCreationDurationEventType && evt.CreatedAt >= windowStart && evt.AfterJson != null);
        if (organizationId.HasValue)
        {
            creationEventsQuery = creationEventsQuery.Where(evt => evt.OrganizationId == organizationId.Value);
        }

        var creationEvents = (await creationEventsQuery
            .Select(evt => new CreationMetricProjection(evt.OrganizationId, evt.AfterJson!, evt.CreatedAt))
            .ToListAsync(cancellationToken))
            .Where(evt => allowedOrganizationIds.Contains(evt.OrganizationId))
            .ToList();

        var assignedUsersByJob = assignments
            .GroupBy(item => item.JobId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).ToHashSet());

        var firstViewByJobAndUser = views
            .GroupBy(item => (item.JobId, item.UserId))
            .ToDictionary(group => group.Key, group => group.Min(item => item.ViewedAt));

        var firstAssignedViewByJob = new Dictionary<Guid, DateTimeOffset>();
        foreach (var (jobId, assignedUsers) in assignedUsersByJob)
        {
            var first = assignedUsers
                .Select(userId => firstViewByJobAndUser.GetValueOrDefault((jobId, userId)))
                .Where(viewedAt => viewedAt != default)
                .DefaultIfEmpty()
                .Min();

            if (first != default)
            {
                firstAssignedViewByJob[jobId] = first;
            }
        }

        var statusesByJob = lifecycleEvents
            .Select(evt => new ParsedStatusEvent(evt.ReportId!.Value, ReadStatus(evt.AfterJson), evt.CreatedAt))
            .Where(evt => evt.Status is not null)
            .GroupBy(evt => evt.JobId)
            .ToDictionary(group => group.Key, group => group.OrderBy(evt => evt.CreatedAt).ToList());

        var creationDurationsByOrganization = creationEvents
            .Select(evt => new ParsedCreationMetric(evt.OrganizationId, ReadDurationSeconds(evt.AfterJson)))
            .Where(metric => metric.DurationSeconds.HasValue)
            .GroupBy(metric => metric.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(metric => (double)metric.DurationSeconds!.Value).ToList());

        var organizationNames = organizations.ToDictionary(org => org.Id, org => org.Name);
        var organizationSummaries = organizations
            .Select(org => BuildSummary(
                jobs.Where(job => job.OrganizationId == org.Id).ToList(),
                statusesByJob,
                firstAssignedViewByJob,
                firstViewByJobAndUser,
                creationDurationsByOrganization.GetValueOrDefault(org.Id) ?? [],
                windowDays))
            .Select(summary => new SuperAdminCaseFlowOrganizationSummary(
                summary.OrganizationId,
                organizationNames.GetValueOrDefault(summary.OrganizationId) ?? "Ukendt organisation",
                summary.CaseCount,
                summary.ApprovedCount,
                summary.RejectedCaseCount,
                summary.MedianCaseCreationSeconds,
                summary.MedianCreationToFirstOpenHours,
                summary.MedianEmployeeFillMinutes,
                summary.MedianCreationToApprovalDays,
                summary.FirstPassApprovalRate,
                summary.CompletedWithinOneDayRate,
                summary.CreationSampleSize,
                summary.EmployeeFillSampleSize,
                summary.CycleSampleSize))
            .OrderByDescending(summary => summary.CaseCount)
            .ThenBy(summary => summary.OrganizationName)
            .ToList();

        var allCreationDurations = creationDurationsByOrganization
            .Where(pair => allowedOrganizationIds.Contains(pair.Key))
            .SelectMany(pair => pair.Value)
            .ToList();

        var totals = BuildSummary(
            jobs,
            statusesByJob,
            firstAssignedViewByJob,
            firstViewByJobAndUser,
            allCreationDurations,
            windowDays,
            Guid.Empty);

        return Results.Ok(new SuperAdminCaseFlowAnalyticsResponse(
            windowDays,
            windowStart,
            generatedAt,
            totals,
            organizationSummaries));
    }

    private static SuperAdminCaseFlowSummary BuildSummary(
        IReadOnlyCollection<JobProjection> jobs,
        IReadOnlyDictionary<Guid, List<ParsedStatusEvent>> statusesByJob,
        IReadOnlyDictionary<Guid, DateTimeOffset> firstAssignedViewByJob,
        IReadOnlyDictionary<(Guid JobId, Guid UserId), DateTimeOffset> firstViewByJobAndUser,
        IReadOnlyCollection<double> creationDurations,
        int windowDays,
        Guid? forcedOrganizationId = null)
    {
        var firstOpenHours = new List<double>();
        var employeeFillMinutes = new List<double>();
        var cycleDays = new List<double>();
        var reviewHours = new List<double>();
        var rejectionEventCount = 0;
        var rejectedCases = 0;
        var approvedCount = 0;
        var firstPassApprovedCount = 0;

        foreach (var job in jobs)
        {
            firstAssignedViewByJob.TryGetValue(job.Id, out var firstAssignedView);
            if (firstAssignedView != default && firstAssignedView >= job.CreatedAt)
            {
                firstOpenHours.Add((firstAssignedView - job.CreatedAt).TotalHours);
            }

            var events = statusesByJob.GetValueOrDefault(job.Id) ?? [];
            var rejectionCountForJob = events.Count(evt => string.Equals(evt.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase));
            if (rejectionCountForJob == 0 && string.Equals(job.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                rejectionCountForJob = 1;
            }

            rejectionEventCount += rejectionCountForJob;
            if (rejectionCountForJob > 0)
            {
                rejectedCases++;
            }

            if (job.SubmittedAt is DateTimeOffset submittedAt)
            {
                DateTimeOffset? employeeStart = null;
                if (job.SubmittedByUserId is Guid submittedBy
                    && firstViewByJobAndUser.TryGetValue((job.Id, submittedBy), out var submitterView))
                {
                    employeeStart = submitterView;
                }
                else if (firstAssignedView != default)
                {
                    employeeStart = firstAssignedView;
                }

                if (employeeStart.HasValue && submittedAt >= employeeStart.Value)
                {
                    employeeFillMinutes.Add((submittedAt - employeeStart.Value).TotalMinutes);
                }
            }

            if (!string.Equals(job.Status, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            approvedCount++;
            if (rejectionCountForJob == 0)
            {
                firstPassApprovedCount++;
            }

            var approvalTime = events
                .Where(evt => string.Equals(evt.Status, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(evt => (DateTimeOffset?)evt.CreatedAt)
                .LastOrDefault() ?? job.UpdatedAt;

            if (approvalTime >= job.CreatedAt)
            {
                cycleDays.Add((approvalTime - job.CreatedAt).TotalDays);
            }

            if (job.SubmittedAt is DateTimeOffset submitted && approvalTime >= submitted)
            {
                reviewHours.Add((approvalTime - submitted).TotalHours);
            }
        }

        var currentlyRejectedCount = jobs.Count(job => string.Equals(job.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase));
        var decidedCurrentCount = approvedCount + currentlyRejectedCount;
        var organizationId = forcedOrganizationId
            ?? jobs.Select(job => job.OrganizationId).FirstOrDefault();

        return new SuperAdminCaseFlowSummary(
            organizationId,
            jobs.Count,
            approvedCount,
            rejectedCases,
            rejectionEventCount,
            decidedCurrentCount > 0 ? (double)approvedCount / decidedCurrentCount : null,
            approvedCount > 0 ? (double)firstPassApprovedCount / approvedCount : null,
            approvedCount > 0 ? (double)rejectionEventCount / approvedCount : null,
            Percentile(creationDurations, 0.50),
            Percentile(creationDurations, 0.90),
            creationDurations.Count,
            Percentile(firstOpenHours, 0.50),
            Percentile(firstOpenHours, 0.90),
            firstOpenHours.Count,
            firstOpenHours.Count > 0 ? (double)firstOpenHours.Count(value => value <= 24) / firstOpenHours.Count : null,
            Percentile(employeeFillMinutes, 0.50),
            Percentile(employeeFillMinutes, 0.90),
            employeeFillMinutes.Count,
            Percentile(cycleDays, 0.50),
            Percentile(cycleDays, 0.90),
            cycleDays.Count,
            cycleDays.Count > 0 ? (double)cycleDays.Count(value => value <= 1) / cycleDays.Count : null,
            Percentile(reviewHours, 0.50),
            Percentile(reviewHours, 0.90),
            reviewHours.Count,
            windowDays > 0 ? approvedCount * 30.0 / windowDays : 0);
    }

    private static double? Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static string? ReadStatus(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed historic telemetry and keep the dashboard available.
        }

        return null;
    }

    private static int? ReadDurationSeconds(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("durationSeconds", out var duration)
                && duration.TryGetInt32(out var value)
                && value is >= 1 and <= 86_400)
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed analytics events.
        }

        return null;
    }

    private sealed record OrganizationProjection(Guid Id, string Name);
    private sealed record JobProjection(Guid Id, Guid OrganizationId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, Guid? SubmittedByUserId);
    private sealed record AssignmentProjection(Guid JobId, Guid UserId);
    private sealed record ViewProjection(Guid JobId, Guid UserId, DateTimeOffset ViewedAt);
    private sealed record LifecycleEventProjection(Guid OrganizationId, Guid? ReportId, string AfterJson, DateTimeOffset CreatedAt);
    private sealed record CreationMetricProjection(Guid OrganizationId, string AfterJson, DateTimeOffset CreatedAt);
    private sealed record ParsedStatusEvent(Guid JobId, string? Status, DateTimeOffset CreatedAt);
    private sealed record ParsedCreationMetric(Guid OrganizationId, int? DurationSeconds);
}

public sealed record CaseCreationDurationRequest(IReadOnlyList<Guid> JobIds, int DurationSeconds);

public sealed record SuperAdminCaseFlowAnalyticsResponse(
    int WindowDays,
    DateTimeOffset From,
    DateTimeOffset GeneratedAt,
    SuperAdminCaseFlowSummary Totals,
    IReadOnlyList<SuperAdminCaseFlowOrganizationSummary> Organizations);

public sealed record SuperAdminCaseFlowSummary(
    Guid OrganizationId,
    int CaseCount,
    int ApprovedCount,
    int RejectedCaseCount,
    int RejectionEventCount,
    double? ApprovalRate,
    double? FirstPassApprovalRate,
    double? RejectionsPerApprovedCase,
    double? MedianCaseCreationSeconds,
    double? P90CaseCreationSeconds,
    int CreationSampleSize,
    double? MedianCreationToFirstOpenHours,
    double? P90CreationToFirstOpenHours,
    int FirstOpenSampleSize,
    double? StartedWithin24HoursRate,
    double? MedianEmployeeFillMinutes,
    double? P90EmployeeFillMinutes,
    int EmployeeFillSampleSize,
    double? MedianCreationToApprovalDays,
    double? P90CreationToApprovalDays,
    int CycleSampleSize,
    double? CompletedWithinOneDayRate,
    double? MedianSubmitToApprovalHours,
    double? P90SubmitToApprovalHours,
    int ReviewSampleSize,
    double ApprovedPer30Days);

public sealed record SuperAdminCaseFlowOrganizationSummary(
    Guid OrganizationId,
    string OrganizationName,
    int CaseCount,
    int ApprovedCount,
    int RejectedCaseCount,
    double? MedianCaseCreationSeconds,
    double? MedianCreationToFirstOpenHours,
    double? MedianEmployeeFillMinutes,
    double? MedianCreationToApprovalDays,
    double? FirstPassApprovalRate,
    double? CompletedWithinOneDayRate,
    int CreationSampleSize,
    int EmployeeFillSampleSize,
    int CycleSampleSize);