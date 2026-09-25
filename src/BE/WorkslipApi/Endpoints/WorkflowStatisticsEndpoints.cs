using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints;

public static class WorkflowStatisticsEndpoints
{
    private const string ActiveSegmentEventType = "analytics.employee_active_segment";

    private static readonly IReadOnlyDictionary<string, string> ReasonLabels = new Dictionary<string, string>
    {
        ["missing_required_data"] = "Manglende data",
        ["incorrect_measurement_or_value"] = "Forkert måling eller værdi",
        ["missing_photo_documentation"] = "Manglende foto eller dokumentation",
        ["wrong_control_point"] = "Forkert kontrolpunkt",
        ["incomplete_work"] = "Arbejdet er ikke færdigt",
        ["duplicate_or_wrong_job"] = "Forkert eller dubleret sag",
        ["system_or_ui_issue"] = "System eller brugerflade",
        ["other"] = "Andet",
        ["unclassified"] = "Uklassificeret"
    };

    public static IEndpointRouteBuilder MapWorkflowStatisticsEndpoints(this IEndpointRouteBuilder app)
    {
        var userGroup = app.MapUserGroup("/api/productivity", "productivity");
        userGroup.MapPost("/workflow-active-segment", RecordWorkflowActiveSegmentAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .ExcludeFromDescription();

        var superAdminGroup = app.MapSuperAdminGroup("/api/superadmin/analytics", "superadmin-analytics");
        superAdminGroup.MapGet("/workflow-statistics", GetWorkflowStatisticsAsync)
            .Produces<WorkflowStatisticsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> RecordWorkflowActiveSegmentAsync(
        WorkflowActiveSegmentRequest request,
        ICurrentUserContext currentUser,
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.JobId == Guid.Empty || request.DurationSeconds is < 1 or > 1_800)
        {
            return Results.BadRequest(new { error = "invalid_workflow_active_segment" });
        }

        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid userId)
        {
            return Results.Forbid();
        }

        var jobExists = await dbContext.JobReports
            .AsNoTracking()
            .AnyAsync(job =>
                job.Id == request.JobId
                && job.OrganizationId == organizationId
                && !job.IsSoftDeleted,
                cancellationToken);

        if (!jobExists)
        {
            return Results.BadRequest(new { error = "workflow_active_segment_job_mismatch" });
        }

        var roleBucket = string.Equals(currentUser.Role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
                ? "admin"
                : "employee";

        dbContext.JobEvents.Add(new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ReportId = request.JobId,
            ActorId = userId,
            EventType = ActiveSegmentEventType,
            Summary = "Aktiv workflow-session registreret.",
            BeforeJson = null,
            AfterJson = JsonSerializer.Serialize(new
            {
                durationSeconds = request.DurationSeconds,
                roleBucket
            }),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetWorkflowStatisticsAsync(
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

        var allowedOrganizationIds = organizations.Select(org => org.Id).ToArray();
        var jobs = await dbContext.JobReports
            .AsNoTracking()
            .Where(job =>
                allowedOrganizationIds.Contains(job.OrganizationId)
                && !job.IsSoftDeleted
                && job.CreatedAt >= windowStart
                && job.CreatedAt <= generatedAt)
            .Select(job => new JobProjection(
                job.Id,
                job.OrganizationId,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt,
                job.SubmittedAt,
                job.SubmittedByUserId,
                job.RejectionNote))
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return Results.Ok(new WorkflowStatisticsResponse(
                windowDays,
                windowStart,
                generatedAt,
                EmptySummary(),
                [],
                EmptyDistribution(EmployeeActiveBuckets()),
                EmptyDistribution(AssignedToSubmittedBuckets()),
                EmptyReasons(),
                []));
        }

        var jobIds = jobs.Select(job => job.Id).ToArray();

        var assignments = await dbContext.JobAssignments
            .AsNoTracking()
            .Where(assignment => jobIds.Contains(assignment.ReportId))
            .Select(assignment => new AssignmentProjection(
                assignment.ReportId,
                assignment.UserId,
                assignment.AssignedAt))
            .ToListAsync(cancellationToken);

        var firstAssignmentByJob = assignments
            .GroupBy(item => item.JobId)
            .ToDictionary(group => group.Key, group => group.Min(item => item.AssignedAt));

        var assignmentPairs = assignments
            .Select(item => (item.JobId, item.UserId))
            .ToHashSet();

        var views = await dbContext.JobViews
            .AsNoTracking()
            .Where(view => jobIds.Contains(view.JobId) && view.ViewType == JobViewTypes.New)
            .Select(view => new ViewProjection(view.JobId, view.UserId, view.ViewedAt))
            .ToListAsync(cancellationToken);

        var firstEmployeeViewByJob = views
            .Where(view => assignmentPairs.Contains((view.JobId, view.UserId)))
            .GroupBy(view => view.JobId)
            .ToDictionary(group => group.Key, group => group.Min(item => item.ViewedAt));

        var events = await dbContext.JobEvents
            .AsNoTracking()
            .Where(evt => evt.ReportId != null && jobIds.Contains(evt.ReportId.Value))
            .Select(evt => new EventProjection(
                evt.ReportId!.Value,
                evt.EventType,
                evt.BeforeJson,
                evt.AfterJson,
                evt.CreatedAt))
            .ToListAsync(cancellationToken);

        var statusEventsByJob = events
            .Select(evt => new ParsedStatusEvent(
                evt.JobId,
                ReadString(evt.BeforeJson, "status"),
                ReadString(evt.AfterJson, "status"),
                ReadString(evt.AfterJson, "rejectionNote"),
                evt.CreatedAt))
            .Where(evt => evt.BeforeStatus is not null || evt.AfterStatus is not null)
            .GroupBy(evt => evt.JobId)
            .ToDictionary(group => group.Key, group => group.OrderBy(evt => evt.CreatedAt).ToArray());

        var activeSegmentsByJob = events
            .Where(evt => string.Equals(evt.EventType, ActiveSegmentEventType, StringComparison.OrdinalIgnoreCase))
            .Select(evt => new ActiveSegment(
                evt.JobId,
                ReadInt(evt.AfterJson, "durationSeconds"),
                ReadString(evt.AfterJson, "roleBucket"),
                evt.CreatedAt))
            .Where(segment =>
                segment.DurationSeconds is > 0 and <= 1_800
                && string.Equals(segment.RoleBucket, "employee", StringComparison.OrdinalIgnoreCase))
            .GroupBy(segment => segment.JobId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CreatedAt).ToArray());

        var metrics = jobs.Select(job => BuildJobMetric(
            job,
            firstAssignmentByJob.GetValueOrDefault(job.Id),
            firstEmployeeViewByJob.GetValueOrDefault(job.Id),
            statusEventsByJob.GetValueOrDefault(job.Id) ?? [],
            activeSegmentsByJob.GetValueOrDefault(job.Id) ?? [])).ToList();

        var summary = BuildSummary(metrics);
        var trend = metrics
            .GroupBy(metric => StartOfIsoWeek(metric.CreatedAt))
            .OrderBy(group => group.Key)
            .Select(group => BuildTrendPoint(group.Key, group.ToList()))
            .ToList();

        var employeeActiveDistribution = BuildDistribution(
            metrics.Select(metric => metric.EmployeeActiveSeconds),
            EmployeeActiveBuckets());
        var assignedDistribution = BuildDistribution(
            metrics.Select(metric => metric.AssignedToSubmittedSeconds),
            AssignedToSubmittedBuckets());
        var rejectionReasons = BuildReasonDistribution(metrics);

        var organizationNames = organizations.ToDictionary(org => org.Id, org => org.Name);
        var organizationBreakdown = metrics
            .GroupBy(metric => metric.OrganizationId)
            .Select(group =>
            {
                var organizationSummary = BuildSummary(group.ToList());
                return new WorkflowOrganizationSummary(
                    group.Key,
                    organizationNames.GetValueOrDefault(group.Key) ?? "Ukendt organisation",
                    organizationSummary.CaseCount,
                    organizationSummary.SubmittedCount,
                    organizationSummary.ApprovedCount,
                    organizationSummary.FirstPassApprovalRate,
                    organizationSummary.RejectionRate,
                    organizationSummary.CreatedToApproved,
                    organizationSummary.EmployeeActive,
                    organizationSummary.AssignedToSubmitted);
            })
            .OrderByDescending(item => item.CaseCount)
            .ThenBy(item => item.OrganizationName)
            .ToList();

        return Results.Ok(new WorkflowStatisticsResponse(
            windowDays,
            windowStart,
            generatedAt,
            summary,
            trend,
            employeeActiveDistribution,
            assignedDistribution,
            rejectionReasons,
            organizationBreakdown));
    }

    private static JobMetric BuildJobMetric(
        JobProjection job,
        DateTimeOffset assignedAt,
        DateTimeOffset firstWorkAt,
        IReadOnlyCollection<ParsedStatusEvent> statusEvents,
        IReadOnlyCollection<ActiveSegment> activeSegments)
    {
        var rejectionEvents = statusEvents
            .Where(evt =>
                string.Equals(evt.AfterStatus, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(evt.BeforeStatus, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rejectionEvents.Count == 0
            && string.Equals(job.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            rejectionEvents.Add(new ParsedStatusEvent(
                job.Id,
                null,
                JobStatus.Rejected.ToString(),
                job.RejectionNote,
                job.UpdatedAt));
        }

        var isApproved = string.Equals(job.Status, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase);
        DateTimeOffset? finalApprovalAt = null;
        if (isApproved)
        {
            finalApprovalAt = statusEvents
                .Where(evt => string.Equals(evt.AfterStatus, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(evt => (DateTimeOffset?)evt.CreatedAt)
                .LastOrDefault() ?? job.UpdatedAt;
        }

        var submittedAt = job.SubmittedAt;
        var validAssignedAt = assignedAt != default
            && assignedAt >= job.CreatedAt
            && (!submittedAt.HasValue || assignedAt <= submittedAt.Value)
                ? assignedAt
                : job.CreatedAt;
        var validFirstWorkAt = firstWorkAt != default
            && firstWorkAt >= validAssignedAt
            && (!submittedAt.HasValue || firstWorkAt <= submittedAt.Value)
                ? firstWorkAt
                : default;

        var activeSeconds = activeSegments
            .Where(segment => !submittedAt.HasValue || segment.CreatedAt <= submittedAt.Value)
            .Sum(segment => (double)(segment.DurationSeconds ?? 0));

        double? assignedToSubmittedSeconds = submittedAt.HasValue && submittedAt.Value >= validAssignedAt
            ? (submittedAt.Value - validAssignedAt).TotalSeconds
            : null;
        double? assignedToFirstWorkSeconds = validFirstWorkAt != default
            ? (validFirstWorkAt - validAssignedAt).TotalSeconds
            : null;
        double? employeeElapsedSeconds = submittedAt.HasValue && validFirstWorkAt != default
            ? (submittedAt.Value - validFirstWorkAt).TotalSeconds
            : null;
        double? inactiveAfterStartSeconds = assignedToSubmittedSeconds.HasValue
            ? Math.Max(0, assignedToSubmittedSeconds.Value
                - (assignedToFirstWorkSeconds ?? 0)
                - activeSeconds)
            : null;

        var reasonCodes = rejectionEvents
            .Select(evt => ClassifyRejectionReason(evt.RejectionNote))
            .ToArray();

        return new JobMetric(
            job.Id,
            job.OrganizationId,
            job.CreatedAt,
            submittedAt,
            isApproved,
            rejectionEvents.Count,
            reasonCodes,
            finalApprovalAt.HasValue && finalApprovalAt.Value >= job.CreatedAt
                ? (finalApprovalAt.Value - job.CreatedAt).TotalSeconds
                : null,
            activeSeconds > 0 ? activeSeconds : null,
            employeeElapsedSeconds,
            assignedToSubmittedSeconds,
            assignedToFirstWorkSeconds,
            inactiveAfterStartSeconds);
    }

    private static WorkflowStatisticsSummary BuildSummary(IReadOnlyCollection<JobMetric> metrics)
    {
        var submittedCount = metrics.Count(metric => metric.SubmittedAt.HasValue);
        var approvedCount = metrics.Count(metric => metric.IsApproved);
        var rejectedCaseCount = metrics.Count(metric => metric.RejectionCount > 0);
        var rejectionEventCount = metrics.Sum(metric => metric.RejectionCount);
        var firstPassApprovedCount = metrics.Count(metric => metric.IsApproved && metric.RejectionCount == 0);
        var codedReasons = metrics.SelectMany(metric => metric.ReasonCodes).Where(code => code != "unclassified").ToList();
        var systemReasons = codedReasons.Count(code => code == "system_or_ui_issue");

        return new WorkflowStatisticsSummary(
            metrics.Count,
            submittedCount,
            approvedCount,
            rejectedCaseCount,
            rejectionEventCount,
            approvedCount > 0 ? (double)firstPassApprovedCount / approvedCount : null,
            submittedCount > 0 ? (double)rejectedCaseCount / submittedCount : null,
            approvedCount > 0 ? (double)rejectionEventCount / approvedCount : null,
            codedReasons.Count > 0 ? (double)systemReasons / codedReasons.Count : null,
            rejectionEventCount > 0 ? (double)codedReasons.Count / rejectionEventCount : null,
            BuildDuration(metrics.Select(metric => metric.CreatedToApprovedSeconds)),
            BuildDuration(metrics.Select(metric => metric.EmployeeActiveSeconds)),
            BuildDuration(metrics.Select(metric => metric.EmployeeElapsedSeconds)),
            BuildDuration(metrics.Select(metric => metric.AssignedToSubmittedSeconds)),
            BuildDuration(metrics.Select(metric => metric.AssignedToFirstWorkSeconds)),
            BuildDuration(metrics.Select(metric => metric.InactiveAfterStartSeconds)));
    }

    private static WorkflowTrendPoint BuildTrendPoint(DateOnly periodStart, IReadOnlyCollection<JobMetric> metrics)
    {
        var summary = BuildSummary(metrics);
        return new WorkflowTrendPoint(
            periodStart,
            metrics.Count,
            summary.CreatedToApproved.MedianSeconds,
            summary.EmployeeActive.MedianSeconds,
            summary.AssignedToSubmitted.MedianSeconds,
            summary.FirstPassApprovalRate,
            summary.RejectionRate);
    }

    private static DurationMetric BuildDuration(IEnumerable<double?> values)
    {
        var sample = values.Where(value => value.HasValue && value.Value >= 0).Select(value => value!.Value).ToArray();
        if (sample.Length == 0)
        {
            return new DurationMetric(null, null, null, 0);
        }

        return new DurationMetric(
            sample.Average(),
            Percentile(sample, 0.50),
            Percentile(sample, 0.75),
            sample.Length);
    }

    private static IReadOnlyList<DistributionBucket> BuildDistribution(
        IEnumerable<double?> values,
        IReadOnlyList<BucketDefinition> definitions)
    {
        var sample = values.Where(value => value.HasValue && value.Value >= 0).Select(value => value!.Value).ToArray();
        return definitions.Select(definition =>
        {
            var count = sample.Count(value => value >= definition.MinInclusive && value < definition.MaxExclusive);
            return new DistributionBucket(
                definition.Key,
                definition.Label,
                count,
                sample.Length > 0 ? (double)count / sample.Length : 0);
        }).ToList();
    }

    private static IReadOnlyList<RejectionReasonBucket> BuildReasonDistribution(IReadOnlyCollection<JobMetric> metrics)
    {
        var codes = metrics.SelectMany(metric => metric.ReasonCodes).ToArray();
        return ReasonLabels.Select(pair =>
        {
            var count = codes.Count(code => code == pair.Key);
            return new RejectionReasonBucket(
                pair.Key,
                pair.Value,
                count,
                codes.Length > 0 ? (double)count / codes.Length : 0);
        }).Where(bucket => bucket.Count > 0 || bucket.Code == "unclassified").ToList();
    }

    private static string ClassifyRejectionReason(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return "unclassified";

        var normalized = note.Trim().ToLowerInvariant();
        if (ContainsAny(normalized, "system", "app", "ui", "brugerflade", "knap", "gem", "låst", "kan ikke", "virker ikke", "fejl i workslip"))
            return "system_or_ui_issue";
        if (ContainsAny(normalized, "billede", "foto", "dokument", "dokumentation"))
            return "missing_photo_documentation";
        if (ContainsAny(normalized, "måling", "mål", "værdi", "forkert tal", "dimension"))
            return "incorrect_measurement_or_value";
        if (ContainsAny(normalized, "kontrolpunkt", "kontrol punkt", "anlægstype"))
            return "wrong_control_point";
        if (ContainsAny(normalized, "ikke færdig", "ikke afsluttet", "mangler arbejde", "arbejdet mangler"))
            return "incomplete_work";
        if (ContainsAny(normalized, "forkert sag", "dublet", "duplikat", "forkert kunde"))
            return "duplicate_or_wrong_job";
        if (ContainsAny(normalized, "mangler", "udfyld", "felt", "data", "oplysning"))
            return "missing_required_data";
        return "other";
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(value.Contains);

    private static IReadOnlyList<BucketDefinition> EmployeeActiveBuckets() =>
    [
        new("under_1m", "< 1 min", 0, 60),
        new("1_5m", "1–5 min", 60, 300),
        new("5_15m", "5–15 min", 300, 900),
        new("15_60m", "15–60 min", 900, 3_600),
        new("60m_plus", "60+ min", 3_600, double.MaxValue)
    ];

    private static IReadOnlyList<BucketDefinition> AssignedToSubmittedBuckets() =>
    [
        new("under_1h", "< 1 time", 0, 3_600),
        new("1_4h", "1–4 timer", 3_600, 14_400),
        new("4_24h", "4–24 timer", 14_400, 86_400),
        new("1_3d", "1–3 dage", 86_400, 259_200),
        new("3d_plus", "3+ dage", 259_200, double.MaxValue)
    ];

    private static IReadOnlyList<DistributionBucket> EmptyDistribution(IReadOnlyList<BucketDefinition> definitions) =>
        definitions.Select(item => new DistributionBucket(item.Key, item.Label, 0, 0)).ToList();

    private static IReadOnlyList<RejectionReasonBucket> EmptyReasons() =>
        [new("unclassified", ReasonLabels["unclassified"], 0, 0)];

    private static WorkflowStatisticsSummary EmptySummary() =>
        new(0, 0, 0, 0, 0, null, null, null, null, null,
            new(null, null, null, 0),
            new(null, null, null, 0),
            new(null, null, null, 0),
            new(null, null, null, 0),
            new(null, null, null, 0),
            new(null, null, null, 0));

    private static double? Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0) return null;
        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static DateOnly StartOfIsoWeek(DateTimeOffset value)
    {
        var date = DateOnly.FromDateTime(value.UtcDateTime);
        var offset = ((int)value.UtcDateTime.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static string? ReadString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Historic audit rows may be malformed; ignore the individual value.
        }
        return null;
    }

    private static int? ReadInt(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.TryGetInt32(out var value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed analytics events.
        }
        return null;
    }

    private sealed record OrganizationProjection(Guid Id, string Name);
    private sealed record JobProjection(
        Guid Id,
        Guid OrganizationId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? SubmittedAt,
        Guid? SubmittedByUserId,
        string? RejectionNote);
    private sealed record AssignmentProjection(Guid JobId, Guid UserId, DateTimeOffset AssignedAt);
    private sealed record ViewProjection(Guid JobId, Guid UserId, DateTimeOffset ViewedAt);
    private sealed record EventProjection(Guid JobId, string EventType, string? BeforeJson, string? AfterJson, DateTimeOffset CreatedAt);
    private sealed record ParsedStatusEvent(Guid JobId, string? BeforeStatus, string? AfterStatus, string? RejectionNote, DateTimeOffset CreatedAt);
    private sealed record ActiveSegment(Guid JobId, int? DurationSeconds, string? RoleBucket, DateTimeOffset CreatedAt);
    private sealed record JobMetric(
        Guid JobId,
        Guid OrganizationId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SubmittedAt,
        bool IsApproved,
        int RejectionCount,
        IReadOnlyList<string> ReasonCodes,
        double? CreatedToApprovedSeconds,
        double? EmployeeActiveSeconds,
        double? EmployeeElapsedSeconds,
        double? AssignedToSubmittedSeconds,
        double? AssignedToFirstWorkSeconds,
        double? InactiveAfterStartSeconds);
    private sealed record BucketDefinition(string Key, string Label, double MinInclusive, double MaxExclusive);
}

public sealed record WorkflowActiveSegmentRequest(Guid JobId, int DurationSeconds);

public sealed record WorkflowStatisticsResponse(
    int WindowDays,
    DateTimeOffset From,
    DateTimeOffset GeneratedAt,
    WorkflowStatisticsSummary Summary,
    IReadOnlyList<WorkflowTrendPoint> Trend,
    IReadOnlyList<DistributionBucket> EmployeeActiveDistribution,
    IReadOnlyList<DistributionBucket> AssignedToSubmittedDistribution,
    IReadOnlyList<RejectionReasonBucket> RejectionReasons,
    IReadOnlyList<WorkflowOrganizationSummary> Organizations);

public sealed record WorkflowStatisticsSummary(
    int CaseCount,
    int SubmittedCount,
    int ApprovedCount,
    int RejectedCaseCount,
    int RejectionEventCount,
    double? FirstPassApprovalRate,
    double? RejectionRate,
    double? AverageReworkLoops,
    double? SystemUiRejectionRate,
    double? RejectionReasonCoverageRate,
    DurationMetric CreatedToApproved,
    DurationMetric EmployeeActive,
    DurationMetric EmployeeElapsed,
    DurationMetric AssignedToSubmitted,
    DurationMetric AssignedToFirstWork,
    DurationMetric InactiveAfterStart);

public sealed record DurationMetric(
    double? AverageSeconds,
    double? MedianSeconds,
    double? P75Seconds,
    int SampleSize);

public sealed record WorkflowTrendPoint(
    DateOnly PeriodStart,
    int CaseCount,
    double? CreatedToApprovedMedianSeconds,
    double? EmployeeActiveMedianSeconds,
    double? AssignedToSubmittedMedianSeconds,
    double? FirstPassApprovalRate,
    double? RejectionRate);

public sealed record DistributionBucket(string Key, string Label, int Count, double Rate);
public sealed record RejectionReasonBucket(string Code, string Label, int Count, double Rate);

public sealed record WorkflowOrganizationSummary(
    Guid OrganizationId,
    string OrganizationName,
    int CaseCount,
    int SubmittedCount,
    int ApprovedCount,
    double? FirstPassApprovalRate,
    double? RejectionRate,
    DurationMetric CreatedToApproved,
    DurationMetric EmployeeActive,
    DurationMetric AssignedToSubmitted);