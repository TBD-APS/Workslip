using System.Text.Json;
using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Notifications;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class PushNotificationMonitoringTests
{
    [Fact]
    public async Task NoActiveSubscriptions_IsReportedWithoutSensitiveData()
    {
        var repository = new FakeNotificationRepository();
        var logger = new RecordingLogger<PushNotificationProcessor>();
        var processor = CreateProcessor(repository, new FakePushSender(), logger);
        var notification = CreateNotification();

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
        Assert.Empty(repository.StatusUpdates);
        var entry = Assert.Single(
            logger.Entries,
            candidate => candidate.Level == LogLevel.Error
                && candidate.Message.Contains("no active subscriptions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notification.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains(NotificationType.JobAssigned.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(notification.UserId.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFailure_ReportsSanitizedDeliveryCounts()
    {
        var notification = CreateNotification();
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var logger = new RecordingLogger<PushNotificationProcessor>();
        var processor = CreateProcessor(
            repository,
            new FakePushSender(new PushSenderResult(false, "sensitive provider response", false)),
            logger);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(
            logger.Entries,
            candidate => candidate.Level == LogLevel.Error
                && candidate.Message.Contains("temporary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Successful 0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("temporary failures 1", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive provider response", entry.Message, StringComparison.Ordinal);
        Assert.Equal("Pending", Assert.Single(repository.StatusUpdates).Status);
    }

    private static PushNotificationProcessor CreateProcessor(
        FakeNotificationRepository repository,
        FakePushSender sender,
        RecordingLogger<PushNotificationProcessor> logger) =>
        new(repository, sender, new FakeNotificationService(), logger);

    private static NotificationQueueRow CreateNotification() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        NotificationType = NotificationType.JobAssigned.ToString(),
        PayloadJson = JsonSerializer.Serialize(
            new NotificationPayload(
                Guid.NewGuid(),
                "123",
                "Address",
                NotificationType.JobAssigned.ToString(),
                "Recipient",
                "/app/job/123"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Status = "Processing",
        RetryCount = 0,
        CreatedUtc = DateTimeOffset.UtcNow,
        ProcessingStartedUtc = DateTimeOffset.UtcNow,
        NextAttemptUtc = DateTimeOffset.UtcNow
    };

    private static PushSubscriptionRow CreateSubscription(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Endpoint = "https://push.example/subscription",
        P256Dh = "p256dh",
        Auth = "auth",
        IsActive = true,
        CreatedUtc = DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    private sealed class FakePushSender(PushSenderResult? result = null) : IPushSender
    {
        public Task<PushSenderResult> SendNotificationAsync(
            PushSubscriptionRow subscription,
            string payloadJson,
            CancellationToken cancellationToken) =>
            Task.FromResult(result ?? new PushSenderResult(true, null, false));
    }

    private sealed class FakeNotificationRepository(
        params PushSubscriptionRow[] subscriptions) : INotificationRepository
    {
        public List<StatusUpdate> StatusUpdates { get; } = [];
        public List<Guid> CompletedNotificationIds { get; } = [];
        public HashSet<Guid> SuccessfulSubscriptionIds { get; } = [];

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);

        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken)
        {
            StatusUpdates.Add(new StatusUpdate(status));
            return Task.CompletedTask;
        }

        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken)
        {
            CompletedNotificationIds.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PushSubscriptionRow>>(subscriptions.Where(subscription => subscription.UserId == userId).ToArray());

        public Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(SuccessfulSubscriptionIds);

        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken)
        {
            if (log.Success) SuccessfulSubscriptionIds.Add(log.SubscriptionId);
            return Task.CompletedTask;
        }

        public Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, string? replacedEndpoint, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);
        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed record StatusUpdate(string Status);

    private sealed class FakeNotificationService : INotificationService
    {
        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("Title", "Body");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
