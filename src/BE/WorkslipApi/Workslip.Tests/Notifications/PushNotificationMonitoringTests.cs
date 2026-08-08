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
    private const string NoSubscriptionsTemplate = "Push notification {NotificationId} has no active subscriptions. NotificationType {NotificationType}. RetryCount {RetryCount}.";
    private const string RetryTemplate = "Push delivery will be retried for notification {NotificationId}. NotificationType {NotificationType}. SuccessfulCount {SuccessfulCount}. ExpiredCount {ExpiredCount}. TemporaryFailureCount {TemporaryFailureCount}. RetryCount {RetryCount}.";
    private const string SuccessTemplate = "Push delivery completed for notification {NotificationId} of type {NotificationType}. Successful {SuccessfulCount}.";
    private const string ExpiredTemplate = "Push delivery completed with expired subscriptions for notification {NotificationId} of type {NotificationType}. Successful {SuccessfulCount}, expired {ExpiredCount}.";
    private const string PermanentFailureTemplate = "Push delivery permanently failed for notification {NotificationId}. NotificationType {NotificationType}. SuccessfulCount {SuccessfulCount}. ExpiredCount {ExpiredCount}. TemporaryFailureCount {TemporaryFailureCount}. RetryCount {RetryCount}.";
    private const string InvalidPayloadTemplate = "Push notification {NotificationId} has an invalid payload. FailureType {FailureType}.";
    private const string MissingPayloadTemplate = "Push notification {NotificationId} has a missing payload.";
    private const string UnknownTypeTemplate = "Push notification {NotificationId} has an unknown notification type.";
    private const string UnexpectedRetryTemplate = "Push notification {NotificationId} was rescheduled after an unexpected processing failure. RetryCount {RetryCount}. FailureType {FailureType}.";
    private const string UnexpectedFailureTemplate = "Push notification {NotificationId} failed after an unexpected processing failure. RetryCount {RetryCount}. FailureType {FailureType}.";

    [Fact]
    public async Task NoActiveSubscriptions_IsDebugWithoutSensitiveData()
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
            candidate => candidate.Level == LogLevel.Debug
                && candidate.Message.Contains("no active subscriptions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notification.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains(NotificationType.JobAssigned.ToString(), entry.Message, StringComparison.Ordinal);
        AssertLogContract(
            entry,
            NoSubscriptionsTemplate,
            ("NotificationId", notification.Id),
            ("NotificationType", notification.NotificationType),
            ("RetryCount", notification.RetryCount));
        AssertSafe(entry, notification);
    }

    [Fact]
    public async Task RetryableProviderFailure_HasOneSanitizedWarningOutcome()
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
            candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("delivery completed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("SuccessfulCount 0", entry.Message, StringComparison.Ordinal);
        Assert.Contains("TemporaryFailureCount 1", entry.Message, StringComparison.Ordinal);
        AssertLogContract(
            entry,
            RetryTemplate,
            ("NotificationId", notification.Id),
            ("NotificationType", notification.NotificationType),
            ("SuccessfulCount", 0),
            ("ExpiredCount", 0),
            ("TemporaryFailureCount", 1),
            ("RetryCount", 1));
        AssertSafe(entry, notification, "sensitive provider response");
        Assert.Equal("Pending", Assert.Single(repository.StatusUpdates).Status);
    }

    [Fact]
    public async Task SuccessfulDelivery_HasDebugOnlyOutcome()
    {
        var notification = CreateNotification();
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId));
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(repository, new FakePushSender(), logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, entry =>
            entry.Level == LogLevel.Debug
            && entry.Message.Contains("completed", StringComparison.OrdinalIgnoreCase));
        AssertLogContract(
            entry,
            SuccessTemplate,
            ("NotificationId", notification.Id),
            ("NotificationType", notification.NotificationType),
            ("SuccessfulCount", 1));
        AssertSafe(entry, notification);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ExpiredSubscription_HasOneSanitizedWarningOutcome()
    {
        var notification = CreateNotification();
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId));
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(
                repository,
                new FakePushSender(new PushSenderResult(false, "provider body with token-secret", true)),
                logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("expired", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertLogContract(
            entry,
            ExpiredTemplate,
            ("NotificationId", notification.Id),
            ("NotificationType", notification.NotificationType),
            ("SuccessfulCount", 0),
            ("ExpiredCount", 1));
        AssertSafe(entry, notification, "provider body with token-secret");
    }

    [Fact]
    public async Task ExhaustedProviderFailure_HasOneSanitizedErrorOutcome()
    {
        var notification = CreateNotification(retryCount: 4);
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId));
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(
                repository,
                new FakePushSender(new PushSenderResult(false, "provider body with token-secret", false)),
                logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("delivery completed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("permanently failed", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertLogContract(
            entry,
            PermanentFailureTemplate,
            ("NotificationId", notification.Id),
            ("NotificationType", notification.NotificationType),
            ("SuccessfulCount", 0),
            ("ExpiredCount", 0),
            ("TemporaryFailureCount", 1),
            ("RetryCount", 5));
        AssertSafe(entry, notification, "provider body with token-secret");
        Assert.Equal("Failed", Assert.Single(repository.StatusUpdates).Status);
    }

    [Fact]
    public async Task InvalidPayload_HasOneSanitizedErrorWithoutException()
    {
        const string sensitivePayload = "{\"recipientName\":\"Sensitive Person\"";
        var notification = CreateNotification(payloadJson: sensitivePayload);
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(new FakeNotificationRepository(), new FakePushSender(), logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("invalid payload", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertLogContract(
            entry,
            InvalidPayloadTemplate,
            ("NotificationId", notification.Id),
            ("FailureType", nameof(JsonException)));
        AssertSafe(entry, notification, "Sensitive Person");
    }

    [Fact]
    public async Task MissingPayload_HasOneSanitizedErrorWithoutException()
    {
        var notification = CreateNotification(payloadJson: "null");
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(new FakeNotificationRepository(), new FakePushSender(), logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("missing payload", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertLogContract(entry, MissingPayloadTemplate, ("NotificationId", notification.Id));
        AssertSafe(entry, notification, notification.PayloadJson);
    }

    [Fact]
    public async Task UnknownNotificationType_HasOneSanitizedErrorWithoutException()
    {
        const string sensitiveUnknownType = "Unknown-sensitive-user-token";
        var notification = CreateNotification();
        notification.NotificationType = sensitiveUnknownType;
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(new FakeNotificationRepository(), new FakePushSender(), logger)
            .ProcessNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("unknown notification type", entry.Message, StringComparison.OrdinalIgnoreCase);
        AssertLogContract(entry, UnknownTypeTemplate, ("NotificationId", notification.Id));
        AssertSafe(entry, notification, sensitiveUnknownType);
    }

    [Fact]
    public async Task UnexpectedFailure_HasOneSanitizedWarningWithoutException()
    {
        const string sensitiveException = "endpoint=https://secret.example token=secret-token";
        var notification = CreateNotification();
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId))
        {
            ClaimedNotifications = [notification]
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(repository, new FakePushSender(exception: new InvalidOperationException(sensitiveException)), logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("FailureType InvalidOperationException", entry.Message, StringComparison.Ordinal);
        AssertLogContract(
            entry,
            UnexpectedRetryTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 1),
            ("FailureType", nameof(InvalidOperationException)));
        AssertSafe(entry, notification, sensitiveException);
    }

    [Fact]
    public async Task ExhaustedUnexpectedFailure_HasOneSanitizedErrorWithoutException()
    {
        const string sensitiveException = "endpoint=https://secret.example token=secret-token";
        var notification = CreateNotification(retryCount: 4);
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId))
        {
            ClaimedNotifications = [notification]
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(repository, new FakePushSender(exception: new InvalidOperationException(sensitiveException)), logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("FailureType InvalidOperationException", entry.Message, StringComparison.Ordinal);
        AssertLogContract(
            entry,
            UnexpectedFailureTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 5),
            ("FailureType", nameof(InvalidOperationException)));
        AssertSafe(entry, notification, sensitiveException);
        Assert.Equal("Failed", Assert.Single(repository.StatusUpdates).Status);
    }

    [Fact]
    public async Task PoisonStatusWriteFailure_HasOnlyUnexpectedFailureOutcome()
    {
        var notification = CreateNotification(payloadJson: "{");
        var repository = new FakeNotificationRepository
        {
            ClaimedNotifications = [notification],
            RemainingStatusUpdateFailures = 1
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(repository, new FakePushSender(), logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("invalid payload", StringComparison.OrdinalIgnoreCase));
        AssertLogContract(
            entry,
            UnexpectedRetryTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 1),
            ("FailureType", nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task CompletionWriteFailure_HasOnlyUnexpectedFailureOutcome()
    {
        var notification = CreateNotification();
        var repository = new FakeNotificationRepository
        {
            ClaimedNotifications = [notification],
            RemainingCompletionFailures = 1
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(repository, new FakePushSender(), logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("no active subscriptions", StringComparison.OrdinalIgnoreCase));
        AssertLogContract(
            entry,
            UnexpectedRetryTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 1),
            ("FailureType", nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task RetryStatusWriteFailure_HasOnlyUnexpectedFailureOutcome()
    {
        var notification = CreateNotification();
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId))
        {
            ClaimedNotifications = [notification],
            RemainingStatusUpdateFailures = 1
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(
                repository,
                new FakePushSender(new PushSenderResult(false, "provider detail", false)),
                logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("delivery will be retried", StringComparison.OrdinalIgnoreCase));
        AssertLogContract(
            entry,
            UnexpectedRetryTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 1),
            ("FailureType", nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task ExhaustedStatusWriteFailure_HasOnlyUnexpectedFailureOutcome()
    {
        var notification = CreateNotification(retryCount: 4);
        var repository = new FakeNotificationRepository(CreateSubscription(notification.UserId))
        {
            ClaimedNotifications = [notification],
            RemainingStatusUpdateFailures = 1
        };
        var logger = new RecordingLogger<PushNotificationProcessor>();

        await CreateProcessor(
                repository,
                new FakePushSender(new PushSenderResult(false, "provider detail", false)),
                logger)
            .ProcessBatchAsync(1, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, candidate => candidate.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.DoesNotContain(logger.Entries, candidate =>
            candidate.Message.Contains("permanently failed", StringComparison.OrdinalIgnoreCase));
        AssertLogContract(
            entry,
            UnexpectedFailureTemplate,
            ("NotificationId", notification.Id),
            ("RetryCount", 5),
            ("FailureType", nameof(InvalidOperationException)));
    }

    private static void AssertLogContract(
        LogEntry entry,
        string expectedTemplate,
        params (string Name, object? Value)[] expectedProperties)
    {
        var template = Assert.Single(
            entry.Properties,
            property => property.Key == "{OriginalFormat}");
        Assert.Equal(expectedTemplate, template.Value);

        var actualProperties = entry.Properties
            .Where(property => property.Key != "{OriginalFormat}")
            .ToDictionary(property => property.Key, property => property.Value);
        Assert.Equal(expectedProperties.Length, actualProperties.Count);
        foreach (var (name, value) in expectedProperties)
        {
            Assert.True(actualProperties.TryGetValue(name, out var actualValue), $"Missing structured property '{name}'.");
            Assert.Equal(value, actualValue);
        }
    }

    private static void AssertSafe(
        LogEntry entry,
        NotificationQueueRow notification,
        params string[] sensitiveValues)
    {
        var serializedProperties = string.Join('|', entry.Properties.Select(property =>
            $"{property.Key}={property.Value}"));

        var prohibitedValues = new List<string>(sensitiveValues)
        {
            notification.UserId.ToString(),
            notification.PayloadJson
        };
        try
        {
            var payload = JsonSerializer.Deserialize<NotificationPayload>(
                notification.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (payload is not null)
            {
                prohibitedValues.AddRange([
                    payload.JobId.ToString(),
                    payload.JobNumber,
                    payload.CustomerAddress,
                    payload.RecipientName,
                    payload.Url
                ]);
                if (payload.RejectionNote is not null)
                {
                    prohibitedValues.Add(payload.RejectionNote);
                }
            }
        }
        catch (JsonException)
        {
            // The malformed payload itself is already checked above.
        }

        foreach (var prohibitedValue in prohibitedValues.Where(value => !string.IsNullOrEmpty(value)))
        {
            Assert.DoesNotContain(prohibitedValue, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(prohibitedValue, serializedProperties, StringComparison.Ordinal);
        }
        Assert.Null(entry.Exception);
    }

    private static PushNotificationProcessor CreateProcessor(
        FakeNotificationRepository repository,
        FakePushSender sender,
        RecordingLogger<PushNotificationProcessor> logger) =>
        new(repository, sender, new FakeNotificationService(), logger);

    private static NotificationQueueRow CreateNotification(
        string? payloadJson = null,
        int retryCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        NotificationType = NotificationType.JobAssigned.ToString(),
        PayloadJson = payloadJson ?? JsonSerializer.Serialize(
            new NotificationPayload(
                Guid.NewGuid(),
                "SENSITIVE-JOB-NUMBER-9F4A2C7E",
                "SENSITIVE-CUSTOMER-ADDRESS-9F4A2C7E",
                NotificationType.JobAssigned.ToString(),
                "SENSITIVE-RECIPIENT-9F4A2C7E",
                "/app/job/sensitive-test-route-9f4a2c7e"),
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
        Endpoint = "https://push.example/subscription",
        P256Dh = "p256dh",
        Auth = "auth",
        IsActive = true,
        CreatedUtc = DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    private sealed class FakePushSender(
        PushSenderResult? result = null,
        Exception? exception = null) : IPushSender
    {
        public Task<PushSenderResult> SendNotificationAsync(
            PushSubscriptionRow subscription,
            string payloadJson,
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result ?? new PushSenderResult(true, null, false));
        }
    }

    private sealed class FakeNotificationRepository(
        params PushSubscriptionRow[] subscriptions) : INotificationRepository
    {
        public List<StatusUpdate> StatusUpdates { get; } = [];
        public List<Guid> CompletedNotificationIds { get; } = [];
        public HashSet<Guid> SuccessfulSubscriptionIds { get; } = [];
        public IReadOnlyList<NotificationQueueRow> ClaimedNotifications { get; init; } = [];
        public int RemainingStatusUpdateFailures { get; set; }
        public int RemainingCompletionFailures { get; set; }

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) => Task.FromResult(ClaimedNotifications);

        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken)
        {
            if (RemainingStatusUpdateFailures-- > 0)
            {
                throw new InvalidOperationException("Sensitive repository failure detail.");
            }

            StatusUpdates.Add(new StatusUpdate(status));
            return Task.CompletedTask;
        }

        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken)
        {
            if (RemainingCompletionFailures-- > 0)
            {
                throw new InvalidOperationException("Sensitive repository failure detail.");
            }

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
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToArray()
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties,
        Exception? Exception);
}
