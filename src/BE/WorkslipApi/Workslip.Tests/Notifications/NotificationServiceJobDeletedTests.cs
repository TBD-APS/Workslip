using System.Text.Json;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class NotificationServiceJobDeletedTests
{
    [Fact]
    public async Task QueueJobDeletedAsync_QueuesListNavigationPayload()
    {
        var repository = new CapturingNotificationRepository();
        var service = new NotificationService(repository);
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        await service.QueueJobDeletedAsync(
            userId,
            "Rasmus",
            jobId,
            "0042",
            "Arbejdsadresse 2",
            CancellationToken.None);

        var queued = Assert.IsType<NotificationQueueRow>(repository.QueuedNotification);
        Assert.Equal(userId, queued.UserId);
        Assert.Equal(NotificationType.JobDeleted.ToString(), queued.NotificationType);
        Assert.Equal("Pending", queued.Status);

        var payload = JsonSerializer.Deserialize<NotificationPayload>(
            queued.PayloadJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(payload);
        Assert.Equal(jobId, payload.JobId);
        Assert.Equal("0042", payload.JobNumber);
        Assert.Equal("Arbejdsadresse 2", payload.CustomerAddress);
        Assert.Equal("Rasmus", payload.RecipientName);
        Assert.Equal(NotificationType.JobDeleted.ToString(), payload.NotificationType);
        Assert.Equal("/app", payload.Url);
    }

    [Fact]
    public void GetLocalizedText_DescribesDeletedAssignedJob()
    {
        var service = new NotificationService(new CapturingNotificationRepository());

        var text = service.GetLocalizedText(
            NotificationType.JobDeleted,
            "0042",
            "Arbejdsadresse 2",
            "Rasmus");

        Assert.Equal("SAG-0042 slettet", text.Title);
        Assert.Equal(
            "Rasmus, SAG-0042, som var tildelt dig, er blevet slettet.\nAdresse: Arbejdsadresse 2",
            text.Body);
    }

    private sealed class CapturingNotificationRepository : INotificationRepository
    {
        internal NotificationQueueRow? QueuedNotification { get; private set; }

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
        {
            QueuedNotification = row;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);

        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PushSubscriptionRow>>([]);

        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RegisterSubscriptionAsync(
            Guid userId,
            string endpoint,
            string p256Dh,
            string auth,
            string? userAgent,
            string? replacedEndpoint,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);

        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
