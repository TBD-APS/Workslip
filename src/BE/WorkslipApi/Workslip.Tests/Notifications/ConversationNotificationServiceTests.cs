using System.Text.Json;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class ConversationNotificationServiceTests
{
    [Fact]
    public async Task Action_request_queues_actor_aware_notification_with_exact_message_deep_link()
    {
        var repository = new RecordingNotificationRepository();
        var service = new NotificationService(repository);
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await service.QueueConversationActionRequestedAsync(
            userId,
            "Mikkel",
            jobId,
            "0011",
            "Roskildevej 5",
            "Rasmus",
            "Bekræft modtaget",
            messageId,
            CancellationToken.None);

        var row = Assert.Single(repository.Queued);
        Assert.Equal(userId, row.UserId);
        Assert.Equal(NotificationType.ConversationActionRequested.ToString(), row.NotificationType);

        var payload = JsonSerializer.Deserialize<NotificationPayload>(
            row.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal("Rasmus", payload.ActorName);
        Assert.Equal("Bekræft modtaget", payload.ActionLabel);
        Assert.Equal(messageId, payload.MessageId);
        Assert.Equal($"/app/job/{jobId}?conversation=1&message={messageId}", payload.Url);

        var (title, body) = service.GetLocalizedText(NotificationType.ConversationActionRequested, payload);
        Assert.Contains("Rasmus", title);
        Assert.Contains("SAG-0011", title);
        Assert.Contains("Bekræft modtaget", body);
    }

    [Fact]
    public async Task Mention_uses_conversation_intent_without_copying_message_body_into_notification_payload()
    {
        var repository = new RecordingNotificationRepository();
        var service = new NotificationService(repository);
        var jobId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await service.QueueConversationMentionAsync(
            Guid.NewGuid(),
            "Mikkel",
            jobId,
            "0011",
            "Roskildevej 5",
            "Rasmus",
            messageId,
            CancellationToken.None);

        var row = Assert.Single(repository.Queued);
        Assert.Equal(NotificationType.ConversationMention.ToString(), row.NotificationType);
        Assert.DoesNotContain("message body", row.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Roskildevej 5\n", row.PayloadJson, StringComparison.Ordinal);
    }

    private sealed class RecordingNotificationRepository : INotificationRepository
    {
        public List<NotificationQueueRow> Queued { get; } = [];

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
        {
            Queued.Add(row);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(Guid notificationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, string? replacedEndpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
