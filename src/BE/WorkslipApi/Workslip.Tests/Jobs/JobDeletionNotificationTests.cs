using System.Text.Json;
using Ardalis.Result;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobDeletionNotificationTests
{
    [Fact]
    public async Task DeleteAsync_QueuesDeletedNotificationForOtherAssignedUsers()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var deletingUserId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var notificationRepository = new RecordingNotificationRepository();
        var notificationService = new NotificationService(notificationRepository);
        var jobRepository = new StubJobRepository(
            CreateJob(
                jobId,
                organizationId,
                [
                    new AssignedUserResponse(deletingUserId, "Slettende admin"),
                    new AssignedUserResponse(recipientId, "Modtager")
                ]),
            JobDeleteRepositoryResult.Deleted());

        var services = new ServiceCollection();
        services.AddHybridCache();
        using var serviceProvider = services.BuildServiceProvider();
        var service = CreateService(
            jobRepository,
            notificationService,
            new TestCurrentUserContext(deletingUserId, organizationId),
            serviceProvider.GetRequiredService<HybridCache>());

        var result = await service.DeleteAsync(jobId, CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        var queued = Assert.Single(notificationRepository.QueuedNotifications);
        Assert.Equal(recipientId, queued.UserId);
        Assert.Equal(NotificationType.JobDeleted.ToString(), queued.NotificationType);
        Assert.Equal("Pending", queued.Status);

        var payload = JsonSerializer.Deserialize<NotificationPayload>(queued.PayloadJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(payload);
        Assert.Equal(jobId, payload.JobId);
        Assert.Equal("0096", payload.JobNumber);
        Assert.Equal("Slettevej 96", payload.CustomerAddress);
        Assert.Equal(NotificationType.JobDeleted.ToString(), payload.NotificationType);
        Assert.Equal("Modtager", payload.RecipientName);
        Assert.Equal("/app", payload.Url);

        var (title, body) = notificationService.GetLocalizedText(
            NotificationType.JobDeleted,
            payload.JobNumber,
            payload.CustomerAddress,
            payload.RecipientName);

        Assert.Equal("SAG-0096 slettet", title);
        Assert.Contains("som var tildelt dig, er blevet slettet", body, StringComparison.Ordinal);
        Assert.Contains("Adresse: Slettevej 96", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotQueueNotificationWhenDeletionIsBlocked()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var deletingUserId = Guid.NewGuid();
        var notificationRepository = new RecordingNotificationRepository();
        var notificationService = new NotificationService(notificationRepository);
        var jobRepository = new StubJobRepository(
            CreateJob(
                jobId,
                organizationId,
                [new AssignedUserResponse(Guid.NewGuid(), "Modtager")]),
            JobDeleteRepositoryResult.BlockedByWorksheets(1));

        var services = new ServiceCollection();
        services.AddHybridCache();
        using var serviceProvider = services.BuildServiceProvider();
        var service = CreateService(
            jobRepository,
            notificationService,
            new TestCurrentUserContext(deletingUserId, organizationId),
            serviceProvider.GetRequiredService<HybridCache>());

        var result = await service.DeleteAsync(jobId, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Empty(notificationRepository.QueuedNotifications);
    }

    private static JobService CreateService(
        IJobRepository jobRepository,
        INotificationService notificationService,
        ICurrentUserContext currentUser,
        HybridCache cache) => new(
            jobRepository,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            cache,
            null!,
            null!,
            null!,
            currentUser,
            NullLogger<JobService>.Instance,
            null!,
            notificationService);

    private static JobReportResponse CreateJob(
        Guid jobId,
        Guid organizationId,
        IReadOnlyList<AssignedUserResponse> assignedUsers)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobReportResponse(
            jobId,
            organizationId,
            "Testorganisation",
            "12345678",
            new CustomerInfo(null, "Testkunde", "Kundevej 1", null, null, null),
            "0096",
            "Slettevej 96",
            "8000",
            "Aarhus C",
            JobStatus.Draft,
            null,
            JobType.Diverse,
            "Test af sletningsnotifikation",
            null,
            null,
            Array.Empty<InstallationTypeResponse>(),
            null,
            null,
            Array.Empty<ClosureFlagResponse>(),
            Array.Empty<JobLinkInfoResponse>(),
            now,
            now,
            null,
            assignedUsers,
            Array.Empty<WorksheetUserGroupResponse>(),
            false,
            null,
            null,
            null);
    }

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = Roles.Admin;
    }

    private sealed class StubJobRepository(
        JobReportResponse job,
        JobDeleteRepositoryResult deleteResult) : IJobRepository
    {
        public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<JobReportResponse?>(id == job.Id && organizationId == job.OrganizationId ? job : null);

        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(deleteResult);

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobTransitionResult?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, string? rejectionNote, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingNotificationRepository : INotificationRepository
    {
        internal List<NotificationQueueRow> QueuedNotifications { get; } = [];

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
        {
            QueuedNotifications.Add(row);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
