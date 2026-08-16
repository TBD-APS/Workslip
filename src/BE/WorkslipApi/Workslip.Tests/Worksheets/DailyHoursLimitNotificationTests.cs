using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Tests.Worksheets;

public sealed class DailyHoursLimitNotificationTests
{
    [Fact]
    public async Task UpsertAsync_notifies_once_when_daily_total_crosses_to_24_hours()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 8, 16);
        var repository = new DailyHoursWorksheetRepository(23.75m, 24m, organizationId);
        var notifications = new RecordingNotificationService();
        var service = CreateService(repository, notifications, organizationId, userId, jobId);

        var result = await service.UpsertAsync(
            new UpsertWorksheetRequest(null, jobId, userId, "Mahad", workDate, 0.25m, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(notifications.DailyHoursCalls);
        var call = notifications.DailyHoursCalls[0];
        Assert.Equal(userId, call.UserId);
        Assert.Equal("Mahad", call.RecipientName);
        Assert.Equal(workDate, call.WorkDate);
        Assert.Equal(24m, call.Hours);
    }

    [Fact]
    public async Task UpsertAsync_does_not_duplicate_notification_when_day_was_already_at_limit()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new DailyHoursWorksheetRepository(24m, 24m, organizationId);
        var notifications = new RecordingNotificationService();
        var service = CreateService(repository, notifications, organizationId, userId, jobId);

        var result = await service.UpsertAsync(
            new UpsertWorksheetRequest(Guid.NewGuid(), jobId, userId, "Mahad", new DateOnly(2026, 8, 16), 1m, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(notifications.DailyHoursCalls);
        Assert.Equal(1, repository.DailyHoursReadCount);
    }

    [Fact]
    public async Task UpsertAsync_does_not_notify_when_persistence_rejects_above_24_hours()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new DailyHoursWorksheetRepository(23.75m, 24m, organizationId, rejectUpsert: true);
        var notifications = new RecordingNotificationService();
        var service = CreateService(repository, notifications, organizationId, userId, jobId);

        var result = await service.UpsertAsync(
            new UpsertWorksheetRequest(null, jobId, userId, "Mahad", new DateOnly(2026, 8, 16), 0.5m, false),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Empty(notifications.DailyHoursCalls);
        Assert.Equal(1, repository.DailyHoursReadCount);
    }

    [Fact]
    public async Task UpsertAsync_remains_successful_when_notification_service_is_not_registered()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new DailyHoursWorksheetRepository(23.75m, 24m, organizationId);
        var service = CreateService(repository, null, organizationId, userId, jobId);

        var result = await service.UpsertAsync(
            new UpsertWorksheetRequest(null, jobId, userId, "Mahad", new DateOnly(2026, 8, 16), 0.25m, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.DailyHoursReadCount);
    }

    [Fact]
    public async Task QueueDailyHoursLimitReachedAsync_queues_timer_navigation_payload()
    {
        var repository = new CapturingNotificationRepository();
        var service = new NotificationService(repository);
        var userId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 8, 16);

        await service.QueueDailyHoursLimitReachedAsync(userId, "Mahad", workDate, 24m, CancellationToken.None);

        var queued = Assert.IsType<NotificationQueueRow>(repository.QueuedNotification);
        Assert.Equal(userId, queued.UserId);
        Assert.Equal(NotificationType.DailyHoursLimitReached.ToString(), queued.NotificationType);

        var payload = JsonSerializer.Deserialize<NotificationPayload>(
            queued.PayloadJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(payload);
        Assert.Equal("/app/timer", payload.Url);
        Assert.Equal(workDate, payload.WorkDate);
        Assert.Equal(24m, payload.Hours);

        var text = service.GetLocalizedText(NotificationType.DailyHoursLimitReached, payload);
        Assert.Equal("Dagens maksimale timer er registreret", text.Title);
        Assert.Contains("24 timer", text.Body);
        Assert.Contains("16-08-2026", text.Body);
    }

    [Fact]
    public async Task QueueDailyHoursLimitReachedAsync_uses_stable_id_per_user_and_day()
    {
        var repository = new CapturingNotificationRepository();
        var service = new NotificationService(repository);
        var userId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 8, 16);

        await service.QueueDailyHoursLimitReachedAsync(userId, "Mahad", workDate, 24m, CancellationToken.None);
        await service.QueueDailyHoursLimitReachedAsync(userId, "Mahad", workDate, 24m, CancellationToken.None);
        await service.QueueDailyHoursLimitReachedAsync(userId, "Mahad", workDate.AddDays(1), 24m, CancellationToken.None);

        Assert.Equal(3, repository.QueuedNotifications.Count);
        Assert.Equal(repository.QueuedNotifications[0].Id, repository.QueuedNotifications[1].Id);
        Assert.NotEqual(repository.QueuedNotifications[0].Id, repository.QueuedNotifications[2].Id);
    }

    private static WorksheetService CreateService(
        IWorksheetRepository repository,
        INotificationService? notifications,
        Guid organizationId,
        Guid userId,
        Guid jobId)
    {
        var job = CreateJob(jobId, organizationId, userId);
        return new WorksheetService(
            repository,
            new StubJobService(job),
            new InlineValidator<UpsertWorksheetRequest>(),
            new StubCurrentUserContext(userId, organizationId, Roles.Admin),
            null!,
            NullLogger<WorksheetService>.Instance,
            notifications);
    }

    private static JobReportSummaryResponse CreateJob(Guid jobId, Guid organizationId, Guid userId) =>
        new(
            Id: jobId,
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            ReportNumber: "0001",
            Status: JobStatus.Draft,
            CustomerId: null,
            CustomerSnapshot: new CustomerSnapshotResponse(null, null, null, null, null),
            DestinationAddress: null,
            DestinationZipCode: null,
            DestinationCity: null,
            JobType: JobType.Diverse.ToString(),
            Work: new JobReportSummaryWorkResponse(null, [], [], null),
            Observations: new JobReportSummaryObservationResponse(null, null, null),
            ControlInstallationTypes: [],
            Links: [],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            SubmittedAt: null,
            AssignedUsers: [new AssignedUserResponse(userId, "Mahad")],
            Worksheets: [],
            TotalHours: null,
            TotalOutlay: null,
            SoftDeleted: false,
            RejectionNote: null);

    private sealed record StubCurrentUserContext(Guid? UserId, Guid? OrganizationId, string? Role) : ICurrentUserContext;

    private sealed class DailyHoursWorksheetRepository(
        decimal before,
        decimal after,
        Guid organizationId,
        bool rejectUpsert = false) : IWorksheetRepository
    {
        public int DailyHoursReadCount { get; private set; }

        public Task<decimal> GetHoursForUserDayAsync(Guid requestedOrganizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            DailyHoursReadCount++;
            return Task.FromResult(DailyHoursReadCount == 1 ? before : after);
        }

        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) =>
            rejectUpsert
                ? throw new WorksheetDailyHoursExceededException()
                : Task.FromResult(new WorksheetResponse(
                    request.Id ?? Guid.NewGuid(),
                    organizationId,
                    request.JobId,
                    request.UserId,
                    request.UserDisplayName,
                    request.WorkDate,
                    request.HoursWorked,
                    request.SleptOnJob,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));

        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubJobService(JobReportSummaryResponse job) : IJobService
    {
        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Result<JobReportSummaryResponse>.Success(job));

        public Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobListResponse>> ListAsync(List<JobStatus>? statuses, string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, string? search, string? sortBy, string? sortDirection, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(Guid UserId, string RecipientName, DateOnly WorkDate, decimal Hours)> DailyHoursCalls { get; } = [];

        public Task QueueDailyHoursLimitReachedAsync(Guid userId, string recipientName, DateOnly workDate, decimal hours, CancellationToken cancellationToken)
        {
            DailyHoursCalls.Add((userId, recipientName, workDate, hours));
            return Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.NotFound());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => throw new NotSupportedException();
    }

    private sealed class CapturingNotificationRepository : INotificationRepository
    {
        public List<NotificationQueueRow> QueuedNotifications { get; } = [];
        public NotificationQueueRow? QueuedNotification => QueuedNotifications.LastOrDefault();

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
        {
            QueuedNotifications.Add(row);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);
        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PushSubscriptionRow>>([]);
        public Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(Guid notificationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, string? replacedEndpoint, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);
        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
