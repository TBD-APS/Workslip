using System.Text.Json;
using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Notifications;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class PushNotificationProcessorTests
{
    [Fact]
    public async Task InvalidPayload_IsFailedWithoutLoggingRawPayload()
    {
        const string sensitivePayload = "{\"customerAddress\":\"Sensitive street 1\"";
        var repository = new FakeNotificationRepository();
        var logger = new RecordingLogger<PushNotificationProcessor>();
        var processor = CreateProcessor(repository, new FakePushSender(), logger);
        var notification = CreateNotification(payloadJson: sensitivePayload);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        var status = Assert.Single(repository.StatusUpdates);
        Assert.Equal("Failed", status.Status);
        Assert.Equal("Invalid notification payload.", status.LastError);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Sensitive street 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoSubscriptions_CompletesWithoutSending()
    {
        var repository = new FakeNotificationRepository();
        var sender = new FakePushSender();
        var processor = CreateProcessor(repository, sender);
        var notification = CreateNotification();

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task SuccessfulDelivery_LogsContractAndCompletes()
    {
        var notification = CreateNotification();
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var sender = new FakePushSender((_, _) => new PushSenderResult(true, null, false));
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        var call = Assert.Single(sender.Calls);
        using var payload = JsonDocument.Parse(call.PayloadJson);
        Assert.Equal("Title", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "Body",
            payload.RootElement.GetProperty("options").GetProperty("body").GetString());
        Assert.Equal(
            "/app/job/123",
            payload.RootElement
                .GetProperty("options")
                .GetProperty("data")
                .GetProperty("url")
                .GetString());
        Assert.Equal(
            $"job-{TestJobId}",
            payload.RootElement.GetProperty("options").GetProperty("tag").GetString());
        Assert.True(Assert.Single(repository.DeliveryLogs).Success);
        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
    }

    [Fact]
    public async Task ExpiredSubscription_IsDisabledAndNotificationCompletes()
    {
        var notification = CreateNotification();
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var sender = new FakePushSender((_, _) =>
            new PushSenderResult(false, "provider detail", true));
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Contains((subscription.Id, false), repository.SubscriptionUpdates);
        var delivery = Assert.Single(repository.DeliveryLogs);
        Assert.False(delivery.Success);
        Assert.Equal("Push subscription expired.", delivery.ErrorMessage);
        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
    }

    [Fact]
    public async Task TemporaryFailure_SchedulesFirstRetryWithoutPersistingProviderDetails()
    {
        var before = DateTimeOffset.UtcNow;
        var notification = CreateNotification();
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var sender = new FakePushSender((_, _) =>
            new PushSenderResult(false, "secret provider detail", false));
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        var status = Assert.Single(repository.StatusUpdates);
        Assert.Equal("Pending", status.Status);
        Assert.Equal(1, status.RetryCount);
        Assert.InRange(
            status.NextAttemptUtc,
            before.AddSeconds(55),
            DateTimeOffset.UtcNow.AddSeconds(65));
        Assert.Equal("Temporary push delivery failure.", status.LastError);
        Assert.Equal(
            "Push provider returned a temporary delivery failure.",
            Assert.Single(repository.DeliveryLogs).ErrorMessage);
    }

    [Fact]
    public async Task FifthTemporaryFailure_IsMarkedFailed()
    {
        var notification = CreateNotification(retryCount: 4);
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var sender = new FakePushSender((_, _) =>
            new PushSenderResult(false, "temporary", false));
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        var status = Assert.Single(repository.StatusUpdates);
        Assert.Equal("Failed", status.Status);
        Assert.Equal(5, status.RetryCount);
        Assert.Equal(
            "Push delivery failed after the maximum retry count.",
            status.LastError);
    }

    [Fact]
    public async Task PartialFailure_RetrySkipsSubscriptionThatAlreadySucceeded()
    {
        var notification = CreateNotification();
        var successful = CreateSubscription(notification.UserId);
        var retrying = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(successful, retrying);
        var retryingHasFailed = true;
        var sender = new FakePushSender((subscription, _) =>
            subscription.Id == retrying.Id && retryingHasFailed
                ? new PushSenderResult(false, "temporary", false)
                : new PushSenderResult(true, null, false));
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);
        retryingHasFailed = false;
        notification.RetryCount = 1;
        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Equal(
            1,
            sender.Calls.Count(call => call.SubscriptionId == successful.Id));
        Assert.Equal(
            2,
            sender.Calls.Count(call => call.SubscriptionId == retrying.Id));
        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
    }

    [Fact]
    public async Task AllSubscriptionsAlreadyDelivered_CompletesWithoutResending()
    {
        var notification = CreateNotification(retryCount: 1);
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        repository.SuccessfulSubscriptionIds.Add(subscription.Id);
        var sender = new FakePushSender();
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Empty(sender.Calls);
        Assert.Contains(notification.Id, repository.CompletedNotificationIds);
    }

    [Fact]
    public async Task UnknownType_IsFailedWithoutSending()
    {
        var notification = CreateNotification();
        notification.NotificationType = "NotARealNotification";
        var subscription = CreateSubscription(notification.UserId);
        var repository = new FakeNotificationRepository(subscription);
        var sender = new FakePushSender();
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessNotificationAsync(notification, CancellationToken.None);

        Assert.Equal(
            "Unknown notification type.",
            Assert.Single(repository.StatusUpdates).LastError);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task UnexpectedFailure_ReschedulesPoisonItemAndContinuesBatch()
    {
        var first = CreateNotification();
        var second = CreateNotification();
        second.UserId = first.UserId;
        var subscription = CreateSubscription(first.UserId);
        var repository = new FakeNotificationRepository(subscription)
        {
            ClaimedNotifications = [first, second]
        };
        var callCount = 0;
        var sender = new FakePushSender((_, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("sender crashed");
            }

            return new PushSenderResult(true, null, false);
        });
        var processor = CreateProcessor(repository, sender);

        var processed = await processor.ProcessBatchAsync(50, CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Contains(
            repository.StatusUpdates,
            update => update.NotificationId == first.Id
                && update.Status == "Pending"
                && update.LastError
                    == "Unexpected push processing failure (InvalidOperationException).");
        Assert.Contains(second.Id, repository.CompletedNotificationIds);
    }

    [Fact]
    public async Task CancelledBatch_PropagatesWithoutChangingNotificationState()
    {
        var repository = new FakeNotificationRepository
        {
            ClaimedNotifications = [CreateNotification()]
        };
        var processor = CreateProcessor(repository, new FakePushSender());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessBatchAsync(50, cancellation.Token));

        Assert.Empty(repository.StatusUpdates);
        Assert.Empty(repository.CompletedNotificationIds);
    }

    private static readonly Guid TestJobId =
        Guid.Parse("00000000-0000-0000-0000-000000000123");

    private static PushNotificationProcessor CreateProcessor(
        FakeNotificationRepository repository,
        FakePushSender sender,
        RecordingLogger<PushNotificationProcessor>? logger = null) =>
        new(
            repository,
            sender,
            new FakeNotificationService(),
            logger ?? new RecordingLogger<PushNotificationProcessor>());

    private static NotificationQueueRow CreateNotification(
        string? payloadJson = null,
        int retryCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        NotificationType = NotificationType.JobAssigned.ToString(),
        PayloadJson = payloadJson ?? JsonSerializer.Serialize(
            new NotificationPayload(
                TestJobId,
                "123",
                "Address",
                NotificationType.JobAssigned.ToString(),
                "Recipient",
                "/app/job/123"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Status = "Processing",
        RetryCount = retryCount,
        CreatedUtc = DateTimeOffset.UtcNow,
        ProcessingStartedUtc = DateTimeOffset.UtcNow,
        NextAttemptUtc = DateTimeOffset.UtcNow
    };

    private static PushSubscriptionRow CreateSubscription(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Endpoint = $"https://push.example/{Guid.NewGuid()}",
        P256Dh = "p256dh",
        Auth = "auth",
        IsActive = true,
        CreatedUtc = DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    private sealed class FakePushSender : IPushSender
    {
        private readonly Func<PushSubscriptionRow, string, PushSenderResult> _send;

        public FakePushSender(
            Func<PushSubscriptionRow, string, PushSenderResult>? send = null)
        {
            _send = send ?? ((_, _) => new PushSenderResult(true, null, false));
        }

        public List<SendCall> Calls { get; } = [];

        public Task<PushSenderResult> SendNotificationAsync(
            PushSubscriptionRow subscription,
            string payloadJson,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new SendCall(subscription.Id, payloadJson));
            return Task.FromResult(_send(subscription, payloadJson));
        }
    }

    private sealed record SendCall(Guid SubscriptionId, string PayloadJson);

    private sealed class FakeNotificationRepository(
        params PushSubscriptionRow[] subscriptions) : INotificationRepository
    {
        public IReadOnlyList<NotificationQueueRow> ClaimedNotifications { get; init; } = [];
        public List<StatusUpdate> StatusUpdates { get; } = [];
        public List<Guid> CompletedNotificationIds { get; } = [];
        public List<NotificationDeliveryLogRow> DeliveryLogs { get; } = [];
        public List<(Guid SubscriptionId, bool IsActive)> SubscriptionUpdates { get; } = [];
        public HashSet<Guid> SuccessfulSubscriptionIds { get; } = [];

        public Task QueueNotificationAsync(
            NotificationQueueRow row,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(
            int batchSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ClaimedNotifications);
        }

        public Task UpdateNotificationStatusAsync(
            Guid id,
            string status,
            int retryCount,
            DateTimeOffset nextAttemptUtc,
            string? lastError,
            CancellationToken cancellationToken)
        {
            StatusUpdates.Add(
                new StatusUpdate(id, status, retryCount, nextAttemptUtc, lastError));
            return Task.CompletedTask;
        }

        public Task MarkNotificationCompletedAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            CompletedNotificationIds.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PushSubscriptionRow>>
            GetActiveSubscriptionsForUserAsync(
                Guid userId,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PushSubscriptionRow>>(
                subscriptions
                    .Where(subscription => subscription.UserId == userId)
                    .ToArray());

        public Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(
            Guid notificationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(SuccessfulSubscriptionIds);

        public Task UpdateSubscriptionActiveStatusAsync(
            Guid subscriptionId,
            bool isActive,
            CancellationToken cancellationToken)
        {
            SubscriptionUpdates.Add((subscriptionId, isActive));
            return Task.CompletedTask;
        }

        public Task LogDeliveryAttemptAsync(
            NotificationDeliveryLogRow log,
            CancellationToken cancellationToken)
        {
            DeliveryLogs.Add(log);
            if (log.Success)
            {
                SuccessfulSubscriptionIds.Add(log.SubscriptionId);
            }

            return Task.CompletedTask;
        }

        public Task RegisterSubscriptionAsync(
            Guid userId,
            string endpoint,
            string p256Dh,
            string auth,
            string? userAgent,
            string? replacedEndpoint,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(
            Guid userId,
            int limit,
            int offset,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);

        public Task MarkReadAsync(
            Guid userId,
            Guid notificationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkAllReadAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteAsync(
            Guid userId,
            Guid notificationId,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed record StatusUpdate(
        Guid NotificationId,
        string Status,
        int RetryCount,
        DateTimeOffset NextAttemptUtc,
        string? LastError);

    private sealed class FakeNotificationService : INotificationService
    {
        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public (string Title, string Body) GetLocalizedText(
            NotificationType notificationType,
            string jobNumber,
            string customerAddress,
            string recipientName,
            string? rejectionNote = null) => ("Title", "Body");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
