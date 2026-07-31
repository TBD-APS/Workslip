from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace(path: str, old: str, new: str) -> None:
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"Expected source not found in {path}: {old[:120]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace(
    "src/BE/WorkslipApi/Workslip.Domain/Models/JobReportRow.cs",
    "    public DateTimeOffset? SubmittedAt { get; set; }\n    public string? RejectionNote { get; set; }",
    "    public DateTimeOffset? SubmittedAt { get; set; }\n    public Guid? SubmittedByUserId { get; set; }\n    public string? RejectionNote { get; set; }",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobContracts.cs",
    "public sealed record JobTransitionResult(JobReportResponse Report, bool Changed);",
    "public sealed record JobTransitionResult(\n    JobReportResponse Report,\n    bool Changed,\n    Guid? SubmittedByUserId);",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    "            return alreadyApplied is null ? null : new JobTransitionResult(alreadyApplied, false);",
    "            return alreadyApplied is null\n                ? null\n                : new JobTransitionResult(alreadyApplied, false, existing.SubmittedByUserId);",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    "        if (nextStatus == JobStatus.InReview && existing.SubmittedAt is null)\n        {\n            entry.Property(e => e.SubmittedAt).CurrentValue = now;\n        }\n\n        entry.Property(e => e.RejectionNote).CurrentValue = nextStatus == JobStatus.Rejected ? rejectionNote : null;",
    "        if (nextStatus == JobStatus.InReview)\n        {\n            if (existing.SubmittedAt is null)\n            {\n                entry.Property(e => e.SubmittedAt).CurrentValue = now;\n            }\n\n            entry.Property(e => e.SubmittedByUserId).CurrentValue = actorId;\n        }\n\n        entry.Property(e => e.RejectionNote).CurrentValue = nextStatus == JobStatus.Rejected ? rejectionNote : null;",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    "        var transitioned = await GetSingleJobAsync(id, organizationId, cancellationToken);\n        await tx.CommitAsync(cancellationToken);\n        return transitioned is null ? null : new JobTransitionResult(transitioned, true);",
    "        var submittedByUserId = entry.Property(e => e.SubmittedByUserId).CurrentValue;\n        var transitioned = await GetSingleJobAsync(id, organizationId, cancellationToken);\n        await tx.CommitAsync(cancellationToken);\n        return transitioned is null\n            ? null\n            : new JobTransitionResult(transitioned, true, submittedByUserId);",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs",
    "        var actorId = currentUser.UserId;\n        var transition = await _jobRepository.TransitionAsync(id, organizationId.Value, targetStatus, actorId, rejectionNote, cancellationToken);",
    "        var actorId = currentUser.UserId;\n        if (actorId is null)\n        {\n            return Result<JobReportSummaryResponse>.Unauthorized();\n        }\n\n        var transition = await _jobRepository.TransitionAsync(id, organizationId.Value, targetStatus, actorId, rejectionNote, cancellationToken);",
)

replace(
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs",
    "        else if (targetStatus == JobStatus.Rejected)\n        {\n            var events = await _jobRepository.GetEventsAsync(id, organizationId.Value, 100, 0, cancellationToken);\n            var submitterEvent = events?.FirstOrDefault(e =>\n                e.ActorId is not null\n                && e.Changes.Any(c => c.PropertyName == \"Status\" && c.After == JobStatus.InReview.ToString()));\n\n            if (submitterEvent?.ActorId is Guid submitterId)\n            {\n                await assignmentRepository.AssignAsync(report.Id, organizationId.Value, [submitterId], actorId, cancellationToken);\n                report = await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken) ?? report;\n                logger.LogInformation(\"Job reassigned to submitter on rejection. JobId: {JobId}. SubmitterId: {SubmitterId}.\", id, submitterId);\n            }\n            else\n            {\n                logger.LogWarning(\"Could not find submitter for rejected job. JobId: {JobId}. Falling back to current assignees.\", id);\n            }\n\n            foreach (var assignedUser in report.AssignedUsers)\n            {\n                if (assignedUser.Id == currentUser.UserId) continue;\n                await notificationService.QueueJobDeniedAsync(assignedUser.Id, assignedUser.DisplayName, report.Id, reportNumber, address, rejectionNote, cancellationToken);\n            }\n        }",
    "        else if (targetStatus == JobStatus.Rejected)\n        {\n            IReadOnlyList<AssignedUserResponse> recipients = [];\n\n            if (transition.SubmittedByUserId is Guid submitterId)\n            {\n                recipients = await assignmentRepository.GetAssignedUsersByIdsAsync(\n                    organizationId.Value,\n                    [submitterId],\n                    cancellationToken);\n\n                if (recipients.Count == 1)\n                {\n                    await assignmentRepository.AssignAsync(\n                        report.Id,\n                        organizationId.Value,\n                        [submitterId],\n                        actorId,\n                        cancellationToken);\n                    report = await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken) ?? report;\n                    logger.LogInformation(\n                        \"Job reassigned to persisted submitter on rejection. JobId: {JobId}. SubmitterId: {SubmitterId}.\",\n                        id,\n                        submitterId);\n                }\n                else\n                {\n                    logger.LogWarning(\n                        \"Persisted submitter was not found in the job organization. JobId: {JobId}. SubmitterId: {SubmitterId}. OrganizationId: {OrganizationId}.\",\n                        id,\n                        submitterId,\n                        organizationId.Value);\n                }\n            }\n\n            if (recipients.Count == 0)\n            {\n                recipients = report.AssignedUsers\n                    .Where(user => user.Id != actorId)\n                    .DistinctBy(user => user.Id)\n                    .ToArray();\n                logger.LogWarning(\n                    \"Rejected job has no valid persisted submitter. Falling back to current assignees. JobId: {JobId}. RecipientCount: {RecipientCount}.\",\n                    id,\n                    recipients.Count);\n            }\n\n            foreach (var recipient in recipients)\n            {\n                if (recipient.Id == actorId) continue;\n                await notificationService.QueueJobDeniedAsync(\n                    recipient.Id,\n                    recipient.DisplayName,\n                    report.Id,\n                    reportNumber,\n                    address,\n                    rejectionNote,\n                    cancellationToken);\n            }\n        }",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs",
    "        entity.Property(e => e.SubmittedAt).HasColumnType(\"datetimeoffset\");",
    "        entity.Property(e => e.SubmittedAt).HasColumnType(\"datetimeoffset\");\n\n        entity.Property(e => e.SubmittedByUserId);",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs",
    "        entity.HasOne(x => x.CustomerRow)\n\n        .WithMany()",
    "        entity.HasOne<UserDataRow>()\n\n            .WithMany()\n\n            .HasForeignKey(e => new { e.OrganizationId, e.SubmittedByUserId })\n\n            .HasPrincipalKey(e => new { e.OrganizationId, e.Id })\n\n            .OnDelete(DeleteBehavior.Restrict)\n\n            .HasConstraintName(\"FK_JobReports_Users_OrganizationId_SubmittedByUserId\");\n\n\n\n        entity.HasOne(x => x.CustomerRow)\n\n        .WithMany()",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs",
    "        entity.HasIndex(e => e.DeletionScheduledAt)\n\n            .HasFilter(\"[DeletionScheduledAt] is not null\")",
    "        entity.HasIndex(e => new { e.OrganizationId, e.SubmittedByUserId })\n\n            .HasFilter(\"[SubmittedByUserId] is not null\")\n\n            .HasDatabaseName(\"IX_JobReports_Organization_SubmittedByUserId\");\n\n\n\n        entity.HasIndex(e => e.DeletionScheduledAt)\n\n            .HasFilter(\"[DeletionScheduledAt] is not null\")",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseSchemaInitializer.cs",
    "            IF COL_LENGTH(N'dbo.NotificationQueue', N'ReadUtc') IS NULL\n                ALTER TABLE [dbo].[NotificationQueue] ADD [ReadUtc] datetimeoffset NULL;",
    "            IF COL_LENGTH(N'dbo.JobReports', N'SubmittedByUserId') IS NULL\n                ALTER TABLE [dbo].[JobReports] ADD [SubmittedByUserId] uniqueidentifier NULL;\n\n            ;WITH LatestSubmission AS\n            (\n                SELECT\n                    jobEvent.OrganizationId,\n                    jobEvent.ReportId,\n                    jobEvent.ActorId,\n                    ROW_NUMBER() OVER\n                    (\n                        PARTITION BY jobEvent.OrganizationId, jobEvent.ReportId\n                        ORDER BY jobEvent.CreatedAt DESC, jobEvent.Id DESC\n                    ) AS SequenceNumber\n                FROM dbo.JobEvents AS jobEvent\n                INNER JOIN dbo.Users AS appUser\n                    ON appUser.OrganizationId = jobEvent.OrganizationId\n                    AND appUser.Id = jobEvent.ActorId\n                WHERE jobEvent.ReportId IS NOT NULL\n                    AND jobEvent.ActorId IS NOT NULL\n                    AND CASE\n                        WHEN ISJSON(jobEvent.AfterJson) = 1\n                        THEN COALESCE(\n                            JSON_VALUE(jobEvent.AfterJson, '$.Status'),\n                            JSON_VALUE(jobEvent.AfterJson, '$.status'))\n                    END = N'InReview'\n            )\n            UPDATE job\n            SET SubmittedByUserId = submission.ActorId\n            FROM dbo.JobReports AS job\n            INNER JOIN LatestSubmission AS submission\n                ON submission.OrganizationId = job.OrganizationId\n                AND submission.ReportId = job.Id\n                AND submission.SequenceNumber = 1\n            WHERE job.SubmittedByUserId IS NULL;\n\n            IF NOT EXISTS\n            (\n                SELECT 1\n                FROM sys.indexes\n                WHERE object_id = OBJECT_ID(N'dbo.JobReports')\n                    AND name = N'IX_JobReports_Organization_SubmittedByUserId'\n            )\n                CREATE INDEX IX_JobReports_Organization_SubmittedByUserId\n                    ON dbo.JobReports (OrganizationId, SubmittedByUserId)\n                    WHERE SubmittedByUserId IS NOT NULL;\n\n            IF COL_LENGTH(N'dbo.NotificationQueue', N'ReadUtc') IS NULL\n                ALTER TABLE [dbo].[NotificationQueue] ADD [ReadUtc] datetimeoffset NULL;",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseIntegrityConstraintsSql.cs",
    "            SELECT @InvalidCount = COUNT(*)\n            FROM dbo.JobReports AS job\n            LEFT JOIN dbo.Customers AS customer",
    "            SELECT @InvalidCount = COUNT(*)\n            FROM dbo.JobReports AS job\n            LEFT JOIN dbo.Users AS submitter\n                ON submitter.OrganizationId = job.OrganizationId\n                AND submitter.Id = job.SubmittedByUserId\n            WHERE job.SubmittedByUserId IS NOT NULL\n                AND submitter.Id IS NULL;\n\n            IF @InvalidCount > 0\n            BEGIN\n                SET @ErrorMessage = CONCAT(\n                    'WOR-245 cannot add the tenant-scoped JobReports-to-submitters FK: found ',\n                    @InvalidCount,\n                    ' orphaned or cross-tenant submission owner reference(s).');\n                THROW 51007, @ErrorMessage, 1;\n            END;\n\n            SELECT @InvalidCount = COUNT(*)\n            FROM dbo.JobReports AS job\n            LEFT JOIN dbo.Customers AS customer",
)

replace(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseIntegrityConstraintsSql.cs",
    "            IF OBJECT_ID(N'dbo.FK_JobReports_Customers_OrganizationId_CustomerId', N'F') IS NULL\n            BEGIN",
    "            IF OBJECT_ID(N'dbo.FK_JobReports_Users_OrganizationId_SubmittedByUserId', N'F') IS NULL\n            BEGIN\n                ALTER TABLE dbo.JobReports WITH CHECK\n                    ADD CONSTRAINT FK_JobReports_Users_OrganizationId_SubmittedByUserId\n                    FOREIGN KEY (OrganizationId, SubmittedByUserId)\n                    REFERENCES dbo.Users (OrganizationId, Id)\n                    ON DELETE NO ACTION;\n                ALTER TABLE dbo.JobReports\n                    CHECK CONSTRAINT FK_JobReports_Users_OrganizationId_SubmittedByUserId;\n            END;\n\n            IF OBJECT_ID(N'dbo.FK_JobReports_Customers_OrganizationId_CustomerId', N'F') IS NULL\n            BEGIN",
)

replace(
    "Docs/architecture/domain-and-dataflows.md",
    "| Jobs | Belong to exactly one organization. A linked customer must have the same `OrganizationId`; the customer snapshot columns are independent value copies and may exist without `CustomerId`. |",
    "| Jobs | Belong to exactly one organization. A linked customer and the latest submission owner must have the same `OrganizationId`; customer snapshot columns remain independent value copies and may exist without `CustomerId`. The latest actor who moves a job to `InReview` is persisted as `SubmittedByUserId` and is the authoritative rejection recipient. |",
)

replace(
    "Docs/architecture/domain-and-dataflows.md",
    "| `JobReports -> Customers` | Restrict / no action | Customer deletion first clears the optional link in the repository; job snapshot values remain unchanged. |",
    "| `JobReports -> Customers` | Restrict / no action | Customer deletion first clears the optional link in the repository; job snapshot values remain unchanged. |\n| `JobReports.(OrganizationId, SubmittedByUserId) -> Users.(OrganizationId, Id)` | Restrict / no action | Rejection routing must retain the latest tenant-valid submission owner. Startup backfills this field from raw `InReview` audit events for legacy jobs; jobs without recoverable ownership remain nullable and use the bounded current-assignee fallback. |",
)

TEST = r'''using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobRejectionNotificationTests
{
    [Fact]
    public async Task RejectingJob_ReassignsAndNotifiesPersistedSubmitter_WithoutReadingHistory()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var submitterId = Guid.NewGuid();
        var repository = new RejectionJobRepository(
            CreateJob(organizationId, JobStatus.InReview, [new AssignedUserResponse(adminId, "Admin")]),
            submitterId);
        var assignments = new RecordingAssignmentRepository(
            organizationId,
            new AssignedUserResponse(submitterId, "Montør"));
        var notifications = new RecordingNotificationService();

        using var services = new ServiceCollection().AddHybridCache().BuildServiceProvider();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.Rejected, "Ret dokumentationen"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.GetEventsCalls);
        Assert.Equal([submitterId], assignments.LastAssignedUserIds);
        var denied = Assert.Single(notifications.Denied);
        Assert.Equal(submitterId, denied.UserId);
        Assert.Equal("Montør", denied.RecipientName);
        Assert.Equal("Ret dokumentationen", denied.RejectionNote);
    }

    [Fact]
    public async Task RejectingLegacyJob_UsesBoundedCurrentAssigneeFallback()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var repository = new RejectionJobRepository(
            CreateJob(organizationId, JobStatus.InReview, [new AssignedUserResponse(assignedUserId, "Montør")]),
            submittedByUserId: null);
        var assignments = new RecordingAssignmentRepository(organizationId, submitter: null);
        var notifications = new RecordingNotificationService();

        using var services = new ServiceCollection().AddHybridCache().BuildServiceProvider();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.Rejected),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.GetEventsCalls);
        Assert.Null(assignments.LastAssignedUserIds);
        Assert.Equal(assignedUserId, Assert.Single(notifications.Denied).UserId);
    }

    private static JobService CreateService(
        IJobRepository repository,
        IAssignmentRepository assignments,
        INotificationService notifications,
        HybridCache cache,
        ICurrentUserContext currentUser) =>
        new(
            repository,
            null!,
            assignments,
            null!,
            new EmptyReferenceDataRepository(),
            null!,
            new EmptyWorksheetRepository(),
            cache,
            null!,
            null!,
            new InlineValidator<ChangeJobStatusRequest>(),
            currentUser,
            NullLogger<JobService>.Instance,
            new JobValidationService(NullLogger<JobValidationService>.Instance),
            notifications,
            null!);

    private static JobReportResponse CreateJob(
        Guid organizationId,
        JobStatus status,
        IReadOnlyList<AssignedUserResponse> assignedUsers) =>
        new(
            Guid.NewGuid(),
            organizationId,
            "Test organization",
            "12345678",
            null,
            "0001",
            "Testvej 1",
            "8000",
            "Aarhus C",
            status,
            null,
            JobType.Diverse,
            null,
            null,
            null,
            [],
            null,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            assignedUsers,
            [],
            false,
            null,
            null,
            null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RejectionJobRepository(
        JobReportResponse job,
        Guid? submittedByUserId) : IJobRepository
    {
        public JobReportResponse Job { get; private set; } = job;
        public int GetEventsCalls { get; private set; }

        public Task<JobReportResponse?> GetSingleJobAsync(
            Guid id,
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobReportResponse?>(
                id == Job.Id && organizationId == Job.OrganizationId ? Job : null);

        public Task<JobTransitionResult?> TransitionAsync(
            Guid id,
            Guid organizationId,
            JobStatus nextStatus,
            Guid? actorId,
            string? rejectionNote,
            CancellationToken cancellationToken)
        {
            Job = Job with { Status = nextStatus, RejectionNote = rejectionNote };
            return Task.FromResult<JobTransitionResult?>(
                new JobTransitionResult(Job, true, submittedByUserId));
        }

        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(
            Guid id,
            Guid organizationId,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            GetEventsCalls++;
            throw new InvalidOperationException("Rejection routing must not read presentation history.");
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAssignmentRepository(
        Guid organizationId,
        AssignedUserResponse? submitter) : IAssignmentRepository
    {
        public IReadOnlyList<Guid>? LastAssignedUserIds { get; private set; }

        public Task AssignAsync(Guid jobId, Guid requestedOrganizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            LastAssignedUserIds = userIds.ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(
            Guid requestedOrganizationId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            IReadOnlyList<AssignedUserResponse> result = submitter is not null && userIds.Contains(submitter.Id)
                ? [submitter]
                : [];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyReferenceDataRepository : IReferenceDataRepository
    {
        public Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ReferenceDataResponse([], [], []));
    }

    private sealed class EmptyWorksheetRepository : IWorksheetRepository
    {
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorksheetResponse>>([]);

        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<DeniedCall> Denied { get; } = [];

        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken)
        {
            Denied.Add(new DeniedCall(userId, recipientName, rejectionNote));
            return Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("", "");
    }

    private sealed record DeniedCall(Guid UserId, string RecipientName, string? RejectionNote);
}
'''

test_path = ROOT / "src/BE/WorkslipApi/Workslip.Tests/Jobs/JobRejectionNotificationTests.cs"
if not test_path.exists():
    test_path.write_text(TEST, encoding="utf-8")
