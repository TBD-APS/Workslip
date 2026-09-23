using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Workslip.Application.Analytics;
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
            .Produces(StatusCodes.Status403Forbidden)
            .ExcludeFromDescription();

        var superAdminGroup = app.MapSuperAdminGroup("/api/superadmin/analytics", "superadmin-analytics");
        superAdminGroup.MapGet("/case-flow", GetCaseFlowAnalyticsAsync)
            .Produces<SuperAdminCaseFlowAnalyticsResponse>()
            .ExcludeFromDescription();
        superAdminGroup.MapGet("/activation-scoreboard", GetActivationScoreboardAsync)
            .Produces<ActivationScoreboardResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .ExcludeFromDescription();

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

        // JobReports is the source of truth for the current case inventory and historic timestamps.
        // Existing jobs therefore produce useful analytics immediately; no synthetic backfill is used.
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
            .Where(evt => evt.ReportId != null && evt.CreatedAt >= windowStart);

        if (organizationId.HasValue)
        {
            lifecycleEventsQuery = lifecycleEventsQuery.Where(evt => evt.OrganizationId == organizationId.Value);
        }

        var lifecycleEvents = (await lifecycleEventsQuery
            .Select(evt => new LifecycleEventProjection(
                evt.OrganizationId,
                evt.ReportId,
                evt.BeforeJson,
                evt.AfterJson,
                evt.CreatedAt))
            .ToListAsync(cancellationToken))
            .Where(evt => evt.ReportId.HasValue && jobIdSet.Contains(evt.ReportId.Value))
            .ToList();

        var creationEventsQuery = dbContext.JobEvents
            .AsNoTracking()
            .Where(evt => evt.EventType == CaseCreationDurationEventType
                && evt.CreatedAt >= windowStart
                && evt.AfterJson != null);

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
            .Select(evt => new ParsedStatusEvent(
                evt.ReportId!.Value,
                ReadStatus(evt.BeforeJson),
                ReadStatus(evt.AfterJson),
                evt.CreatedAt))
            .Where(evt => evt.BeforeStatus is not null || evt.AfterStatus is not null)
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
                summary.SubmittedCount,
                summary.ApprovedCount,
                summary.RejectedCaseCount,
                summary.MedianCaseCreationSeconds,
                summary.MedianCreationToFirstOpenHours,
                summary.MedianCreationToSubmissionHours,
                summary.MedianEmployeeFillMinutes,
                summary.MedianCreationToApprovalDays,
                summary.FirstPassApprovalRate,
                summary.SubmittedWithin24HoursRate,
                summary.CreationSampleSize,
                summary.CreationToSubmissionSampleSize,
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

    private static async Task<IResult> GetActivationScoreboardAsync(
        int? days,
        string? scope,
        IConfiguration configuration,
        IHostEnvironment environment,
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var windowDays = Math.Clamp(days ?? 90, 7, 365);
        var generatedAt = DateTimeOffset.UtcNow;
        var windowStart = generatedAt.AddDays(-windowDays);
        var activeWindowStart = generatedAt.AddDays(-7);

        var selectedScope = string.IsNullOrWhiteSpace(scope)
            ? "customer"
            : scope.Trim().ToLowerInvariant();
        if (selectedScope is not ("customer" or "demo"))
        {
            return Results.BadRequest(new
            {
                error = "invalid_activation_scope",
                allowedScopes = new[] { "customer", "demo" }
            });
        }

        var demoOrganizationIds = new HashSet<Guid>();
        foreach (var configuredId in configuration.GetSection("Analytics:DemoOrganizationIds").Get<string[]>() ?? [])
        {
            if (Guid.TryParse(configuredId, out var parsed))
                demoOrganizationIds.Add(parsed);
        }

        if (selectedScope == "demo" && demoOrganizationIds.Count == 0)
        {
            return Results.Conflict(new
            {
                error = "demo_organization_scope_not_configured",
                configurationKey = "Analytics:DemoOrganizationIds"
            });
        }

        var demoIds = demoOrganizationIds.ToArray();
        var organizationsQuery = dbContext.Organizations
            .AsNoTracking()
            .Where(org => org.Id != PlatformOrganization.Id);

        organizationsQuery = selectedScope == "demo"
            ? organizationsQuery.Where(org => demoIds.Contains(org.Id))
            : organizationsQuery.Where(org => !demoIds.Contains(org.Id));

        var organizations = await organizationsQuery
            .OrderBy(org => org.Name)
            .Select(org => new ActivationOrganizationProjection(org.Id, org.Name, org.CreatedAt))
            .ToListAsync(cancellationToken);

        var organizationIds = organizations.Select(org => org.Id).ToArray();
        if (organizationIds.Length == 0)
        {
            return Results.Ok(new ActivationScoreboardResponse(
                ActivationMetricSemantics.DefinitionVersion,
                generatedAt,
                environment.EnvironmentName,
                selectedScope,
                windowDays,
                EmptyActivationMetrics(),
                EmptyActivationFunnel(),
                new ActivationTelemetryAvailability(
                    "WOR-736",
                    false,
                    ["demo_started", "demo_value_flow_completed", "first_login_completed", "behavior_drop_off"]),
                []));
        }

        var customers = await dbContext.Customers
            .AsNoTracking()
            .Where(customer => organizationIds.Contains(customer.OrganizationId))
            .Select(customer => new ActivationCustomerProjection(
                customer.OrganizationId,
                customer.CreatedAt))
            .ToListAsync(cancellationToken);

        var jobs = await dbContext.JobReports
            .AsNoTracking()
            .Where(job => organizationIds.Contains(job.OrganizationId) && !job.IsSoftDeleted)
            .Select(job => new JobProjection(
                job.Id,
                job.OrganizationId,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt,
                job.SubmittedAt,
                job.SubmittedByUserId))
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToHashSet();
        var approvalEventRows = await dbContext.JobEvents
            .AsNoTracking()
            .Where(evt =>
                organizationIds.Contains(evt.OrganizationId)
                && evt.ReportId != null
                && evt.AfterJson != null
                && evt.AfterJson.Contains("Approved"))
            .Select(evt => new LifecycleEventProjection(
                evt.OrganizationId,
                evt.ReportId,
                evt.BeforeJson,
                evt.AfterJson,
                evt.CreatedAt))
            .ToListAsync(cancellationToken);

        var statusChangesByJob = approvalEventRows
            .Where(evt => evt.ReportId.HasValue && jobIds.Contains(evt.ReportId.Value))
            .Select(evt => new
            {
                JobId = evt.ReportId!.Value,
                Change = new JobStatusChangeObservation(
                    ReadStatus(evt.BeforeJson),
                    ReadStatus(evt.AfterJson),
                    evt.CreatedAt)
            })
            .Where(item => item.Change.AfterStatus is not null)
            .GroupBy(item => item.JobId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<JobStatusChangeObservation>)group
                    .Select(item => item.Change)
                    .OrderBy(change => change.OccurredAt)
                    .ToArray());

        var completions = jobs
            .Select(job => new ActivationCompletionProjection(
                job.Id,
                job.OrganizationId,
                ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
                    job.Status,
                    job.UpdatedAt,
                    statusChangesByJob.GetValueOrDefault(job.Id) ?? [])))
            .Where(item => item.CompletedAt.HasValue)
            .Select(item => item with { CompletedAt = item.CompletedAt!.Value })
            .ToList();

        var completionByJobId = completions.ToDictionary(item => item.JobId);
        var completionsByOrganization = completions
            .GroupBy(item => item.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.CompletedAt).ToArray());

        var firstCustomerByOrganization = customers
            .GroupBy(customer => customer.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => group.Min(customer => customer.CreatedAt));

        var firstJobByOrganization = jobs
            .GroupBy(job => job.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => group.Min(job => job.CreatedAt));

        var activeEvents = await dbContext.JobEvents
            .AsNoTracking()
            .Where(evt =>
                organizationIds.Contains(evt.OrganizationId)
                && evt.CreatedAt >= activeWindowStart)
            .Select(evt => new ActivationActorProjection(
                evt.OrganizationId,
                evt.ActorId))
            .ToListAsync(cancellationToken);

        var activeDate = activeWindowStart.UtcDateTime.Date;
        var activeWorksheets = await dbContext.Worksheets
            .AsNoTracking()
            .Where(worksheet =>
                organizationIds.Contains(worksheet.OrganizationId)
                && worksheet.WorkDate >= activeDate)
            .Select(worksheet => new ActivationWorksheetProjection(
                worksheet.OrganizationId,
                worksheet.UserId))
            .ToListAsync(cancellationToken);

        var activeOrganizationIds = activeEvents.Select(item => item.OrganizationId)
            .Concat(activeWorksheets.Select(item => item.OrganizationId))
            .Concat(jobs.Where(job => job.CreatedAt >= activeWindowStart).Select(job => job.OrganizationId))
            .Distinct()
            .ToHashSet();

        var activeUserIds = activeEvents
            .Where(item => item.ActorId.HasValue)
            .Select(item => item.ActorId!.Value)
            .Concat(activeWorksheets.Select(item => item.UserId))
            .Distinct()
            .ToHashSet();

        var submittedInWindow = jobs
            .Where(job => job.SubmittedAt is DateTimeOffset submittedAt
                && submittedAt >= windowStart
                && submittedAt <= generatedAt)
            .ToList();
        var completedSubmittedInWindow = submittedInWindow.Count(job => completionByJobId.ContainsKey(job.Id));
        var completedInWindow = completions.Count(item =>
            item.CompletedAt >= windowStart && item.CompletedAt <= generatedAt);

        var activatedOrganizations = organizations
            .Where(org => completionsByOrganization.ContainsKey(org.Id))
            .ToList();

        var repeatValueOrganizationIds = completionsByOrganization
            .Where(pair => pair.Value
                .Select(item => ActivationMetricSemantics.StartOfIsoWeek(item.CompletedAt))
                .Distinct()
                .Take(2)
                .Count() >= 2)
            .Select(pair => pair.Key)
            .ToHashSet();

        var timeToFirstValueHours = new List<double>();
        var organizationToCustomerHours = new List<double>();
        var customerToJobHours = new List<double>();
        var jobToValueHours = new List<double>();
        var organizationRows = new List<ActivationOrganizationScore>();

        foreach (var organization in organizations)
        {
            firstCustomerByOrganization.TryGetValue(organization.Id, out var firstCustomerAt);
            firstJobByOrganization.TryGetValue(organization.Id, out var firstJobAt);
            var hasFirstCustomer = firstCustomerByOrganization.ContainsKey(organization.Id);
            var hasFirstJob = firstJobByOrganization.ContainsKey(organization.Id);
            var organizationCompletions = completionsByOrganization.GetValueOrDefault(organization.Id) ?? [];
            var firstCompletedAt = organizationCompletions.FirstOrDefault()?.CompletedAt;

            if (hasFirstCustomer && firstCustomerAt >= organization.CreatedAt)
                organizationToCustomerHours.Add((firstCustomerAt - organization.CreatedAt).TotalHours);

            if (hasFirstCustomer && hasFirstJob && firstJobAt >= firstCustomerAt)
                customerToJobHours.Add((firstJobAt - firstCustomerAt).TotalHours);

            if (hasFirstJob && firstCompletedAt.HasValue && firstCompletedAt.Value >= firstJobAt)
                jobToValueHours.Add((firstCompletedAt.Value - firstJobAt).TotalHours);

            if (firstCompletedAt.HasValue && firstCompletedAt.Value >= organization.CreatedAt)
                timeToFirstValueHours.Add((firstCompletedAt.Value - organization.CreatedAt).TotalHours);

            organizationRows.Add(new ActivationOrganizationScore(
                organization.Id,
                organization.Name,
                organization.CreatedAt,
                hasFirstCustomer ? firstCustomerAt : null,
                hasFirstJob ? firstJobAt : null,
                firstCompletedAt,
                organizationCompletions.Count,
                organizationCompletions.Count(item => item.CompletedAt >= windowStart && item.CompletedAt <= generatedAt),
                repeatValueOrganizationIds.Contains(organization.Id),
                activeOrganizationIds.Contains(organization.Id)));
        }

        var metrics = new ActivationScoreboardMetrics(
            organizations.Count,
            activatedOrganizations.Count,
            activeOrganizationIds.Count,
            activeUserIds.Count,
            completedInWindow,
            submittedInWindow.Count > 0
                ? (double)completedSubmittedInWindow / submittedInWindow.Count
                : null,
            ActivationMetricSemantics.Percentile(timeToFirstValueHours, 0.50),
            ActivationMetricSemantics.Percentile(timeToFirstValueHours, 0.75),
            activatedOrganizations.Count > 0
                ? (double)repeatValueOrganizationIds.Count / activatedOrganizations.Count
                : null);

        var funnel = new ActivationFunnel(
            new ActivationFunnelStage("organization_created", organizations.Count, "domain"),
            new ActivationFunnelStage("first_customer_created", firstCustomerByOrganization.Count, "domain"),
            new ActivationFunnelStage("first_job_created", firstJobByOrganization.Count, "domain"),
            new ActivationFunnelStage("first_completed_compliant_job", activatedOrganizations.Count, "domain"),
            new ActivationFunnelStage("repeat_active_week", repeatValueOrganizationIds.Count, "domain"),
            new ActivationFunnelTiming(
                ActivationMetricSemantics.Percentile(organizationToCustomerHours, 0.50),
                ActivationMetricSemantics.Percentile(organizationToCustomerHours, 0.75)),
            new ActivationFunnelTiming(
                ActivationMetricSemantics.Percentile(customerToJobHours, 0.50),
                ActivationMetricSemantics.Percentile(customerToJobHours, 0.75)),
            new ActivationFunnelTiming(
                ActivationMetricSemantics.Percentile(jobToValueHours, 0.50),
                ActivationMetricSemantics.Percentile(jobToValueHours, 0.75)));

        return Results.Ok(new ActivationScoreboardResponse(
            ActivationMetricSemantics.DefinitionVersion,
            generatedAt,
            environment.EnvironmentName,
            selectedScope,
            windowDays,
            metrics,
            funnel,
            new ActivationTelemetryAvailability(
                "WOR-736",
                false,
                ["demo_started", "demo_value_flow_completed", "first_login_completed", "behavior_drop_off"]),
            organizationRows));
    }

    private static ActivationScoreboardMetrics EmptyActivationMetrics() =>
        new(0, 0, 0, 0, 0, null, null, null, null);

    private static ActivationFunnel EmptyActivationFunnel() =>
        new(
            new("organization_created", 0, "domain"),
            new("first_customer_created", 0, "domain"),
            new("first_job_created", 0, "domain"),
            new("first_completed_compliant_job", 0, "domain"),
            new("repeat_active_week", 0, "domain"),
            new(null, null),
            new(null, null),
            new(null, null));

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
        var creationToSubmissionHours = new List<double>();
        var employeeFillMinutes = new List<double>();
        var cycleDays = new List<double>();
        var reviewHours = new List<double>();

        var submittedCount = 0;
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

            var rejectionCountForJob = events.Count(evt =>
                string.Equals(evt.AfterStatus, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(evt.BeforeStatus, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase));

            if (rejectionCountForJob == 0
                && string.Equals(job.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                rejectionCountForJob = 1;
            }

            rejectionEventCount += rejectionCountForJob;

            if (rejectionCountForJob > 0)
            {
                rejectedCases++;
            }

            if (job.SubmittedAt is DateTimeOffset submittedAt && submittedAt >= job.CreatedAt)
            {
                submittedCount++;
                creationToSubmissionHours.Add((submittedAt - job.CreatedAt).TotalHours);

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

                if (employeeStart.HasValue
                    && submittedAt >= employeeStart.Value
                    && employeeStart.Value >= job.CreatedAt)
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
                .Where(evt => string.Equals(evt.AfterStatus, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
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

        var currentlyRejectedCount = jobs.Count(job =>
            string.Equals(job.Status, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase));

        var decidedCurrentCount = approvedCount + currentlyRejectedCount;

        var organizationId = forcedOrganizationId
            ?? jobs.Select(job => job.OrganizationId).FirstOrDefault();

        return new SuperAdminCaseFlowSummary(
            organizationId,
            jobs.Count,
            submittedCount,
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
            firstOpenHours.Count > 0
                ? (double)firstOpenHours.Count(value => value <= 24) / firstOpenHours.Count
                : null,
            Percentile(creationToSubmissionHours, 0.50),
            Percentile(creationToSubmissionHours, 0.90),
            creationToSubmissionHours.Count,
            creationToSubmissionHours.Count > 0
                ? (double)creationToSubmissionHours.Count(value => value <= 24) / creationToSubmissionHours.Count
                : null,
            Percentile(employeeFillMinutes, 0.50),
            Percentile(employeeFillMinutes, 0.90),
            employeeFillMinutes.Count,
            Percentile(cycleDays, 0.50),
            Percentile(cycleDays, 0.90),
            cycleDays.Count,
            cycleDays.Count > 0
                ? (double)cycleDays.Count(value => value <= 1) / cycleDays.Count
                : null,
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

    private static string? ReadStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

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
            // Historic audit rows may contain malformed JSON; skip only that event.
        }

        return null;
    }

    private static int? ReadDurationSeconds(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "durationSeconds", StringComparison.OrdinalIgnoreCase)
                    && property.Value.TryGetInt32(out var value)
                    && value is >= 1 and <= 86_400)
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
        Guid? SubmittedByUserId);
    private sealed record AssignmentProjection(Guid JobId, Guid UserId);
    private sealed record ViewProjection(Guid JobId, Guid UserId, DateTimeOffset ViewedAt);
    private sealed record LifecycleEventProjection(
        Guid OrganizationId,
        Guid? ReportId,
        string? BeforeJson,
        string? AfterJson,
        DateTimeOffset CreatedAt);
    private sealed record CreationMetricProjection(Guid OrganizationId, string AfterJson, DateTimeOffset CreatedAt);
    private sealed record ParsedStatusEvent(
        Guid JobId,
        string? BeforeStatus,
        string? AfterStatus,
        DateTimeOffset CreatedAt);
    private sealed record ParsedCreationMetric(Guid OrganizationId, int? DurationSeconds);
}

public sealed record ActivationScoreboardResponse(
    string MetricDefinitionVersion,
    DateTimeOffset GeneratedAt,
    string Environment,
    string Scope,
    int WindowDays,
    ActivationScoreboardMetrics Metrics,
    ActivationFunnel Funnel,
    ActivationTelemetryAvailability Telemetry,
    IReadOnlyList<ActivationOrganizationScore> Organizations);

public sealed record ActivationScoreboardMetrics(
    int Companies,
    int ActivatedCompanies,
    int WeeklyActiveCompanies,
    int WeeklyActiveUsers,
    int CompletedCompliantJobs,
    double? ComplianceCompletionRate,
    double? MedianTimeToFirstCompletedCompliantJobHours,
    double? P75TimeToFirstCompletedCompliantJobHours,
    double? RepeatValueRate);

public sealed record ActivationFunnel(
    ActivationFunnelStage OrganizationCreated,
    ActivationFunnelStage FirstCustomerCreated,
    ActivationFunnelStage FirstJobCreated,
    ActivationFunnelStage FirstCompletedCompliantJob,
    ActivationFunnelStage RepeatActiveWeek,
    ActivationFunnelTiming OrganizationToCustomer,
    ActivationFunnelTiming CustomerToJob,
    ActivationFunnelTiming JobToCompletedCompliant);

public sealed record ActivationFunnelStage(string Name, int Companies, string Source);
public sealed record ActivationFunnelTiming(double? MedianHours, double? P75Hours);
public sealed record ActivationTelemetryAvailability(
    string OwnerIssue,
    bool Available,
    IReadOnlyList<string> UnavailableStages);

public sealed record ActivationOrganizationScore(
    Guid OrganizationId,
    string OrganizationName,
    DateTimeOffset OrganizationCreatedAt,
    DateTimeOffset? FirstCustomerCreatedAt,
    DateTimeOffset? FirstJobCreatedAt,
    DateTimeOffset? FirstCompletedCompliantJobAt,
    int CompletedCompliantJobs,
    int CompletedCompliantJobsInWindow,
    bool HasRepeatValueWeek,
    bool IsWeeklyActive);

private sealed record ActivationOrganizationProjection(Guid Id, string Name, DateTimeOffset CreatedAt);
private sealed record ActivationCustomerProjection(Guid OrganizationId, DateTimeOffset CreatedAt);
private sealed record ActivationCompletionProjection(Guid JobId, Guid OrganizationId, DateTimeOffset? CompletedAt);
private sealed record ActivationActorProjection(Guid OrganizationId, Guid? ActorId);
private sealed record ActivationWorksheetProjection(Guid OrganizationId, Guid UserId);

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
    int SubmittedCount,
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
    double? MedianCreationToSubmissionHours,
    double? P90CreationToSubmissionHours,
    int CreationToSubmissionSampleSize,
    double? SubmittedWithin24HoursRate,
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
    int SubmittedCount,
    int ApprovedCount,
    int RejectedCaseCount,
    double? MedianCaseCreationSeconds,
    double? MedianCreationToFirstOpenHours,
    double? MedianCreationToSubmissionHours,
    double? MedianEmployeeFillMinutes,
    double? MedianCreationToApprovalDays,
    double? FirstPassApprovalRate,
    double? SubmittedWithin24HoursRate,
    int CreationSampleSize,
    int CreationToSubmissionSampleSize,
    int EmployeeFillSampleSize,
    int CycleSampleSize);
