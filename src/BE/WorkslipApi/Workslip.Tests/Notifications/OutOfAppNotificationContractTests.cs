using System.Text.Json;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Notifications;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class OutOfAppNotificationContractTests
{
    private static readonly Guid JobId =
        Guid.Parse("00000000-0000-0000-0000-000000000317");

    public static TheoryData<NotificationType, string, string, string> NotificationCases => new()
    {
        { NotificationType.JobAssigned, "SAG-317 tildelt", "tildelt dig", $"/app/job/{JobId}" },
        { NotificationType.JobReadyForReview, "SAG-317 klar til gennemgang", "klar til din gennemgang", $"/app/completed/{JobId}" },
        { NotificationType.JobDenied, "SAG-317 afvist", "Årsag: Mangler dokumentation", $"/app/job/{JobId}" },
        { NotificationType.JobCompleted, "SAG-317 godkendt", "er godkendt", $"/app/completed/{JobId}" },
        { NotificationType.JobUnassigned, "Sag uden medarbejdere", "ingen tildelte medarbejdere", $"/app/job/{JobId}" },
        { NotificationType.JobDeleted, "SAG-317 slettet", "er blevet slettet", "/app" }
    };

    [Theory]
    [MemberData(nameof(NotificationCases))]
    public async Task EveryOutOfAppNotification_UsesSharedQueueProcessorAndPayloadContract(
        NotificationType type,
        string expectedTitle,
        string expectedBodyFragment,
        string expectedUrl)
    {
        var userId = Guid.NewGuid();
        var repository = new ContractRepository(userId);
        var service = new NotificationService(repository);
        await QueueAsync(service, type, userId);
        var queued = Assert.Single(repository.Queued);
        var sender = new RecordingPushSender();
        var processor = new PushNotificationProcessor(
            repository,
            sender,
            service,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PushNotificationProcessor>.Instance);

        await processor.ProcessNotificationAsync(queued, CancellationToken.None);

        Assert.Equal(type.ToString(), queued.NotificationType);
        Assert.Equal(userId, queued.UserId);
        var sent = Assert.Single(sender.Payloads);
        using var json = JsonDocument.Parse(sent);
        var root = json.RootElement;
        var options = root.GetProperty("options");

        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Contains(expectedBodyFragment, options.GetProperty("body").GetString());
        Assert.Contains("Adresse: Testvej 1", options.GetProperty("body").GetString());
        Assert.Equal($"job-{JobId}", options.GetProperty("tag").GetString());
        Assert.Equal(expectedUrl, options.GetProperty("data").GetProperty("url").GetString());
        Assert.Equal("/icons/icon-192.png", options.GetProperty("icon").GetString());
        Assert.Equal("/icons/badge.png", options.GetProperty("badge").GetString());
        Assert.Contains(queued.Id, repository.Completed);
        Assert.True(Assert.Single(repository.DeliveryLogs).Success);
    }

    [Fact]
    public async Task Delivery_GoesOnlyToCurrentEndpointOwner()
    {
        var arneId = Guid.NewGuid();
        var nielsId = Guid.NewGuid();
        var currentSubscription = CreateSubscription(nielsId, "shared-device");
        var repository = new ContractRepository(nielsId, currentSubscription);
        var service = new NotificationService(repository);
        await service.QueueJobDeniedAsync(
            nielsId,
            "Niels Petersen",
            JobId,
            "317",
            "Testvej 1",
            "Mangler dokumentation",
            CancellationToken.None);
        var sender = new RecordingPushSender();
        var processor = new PushNotificationProcessor(
            repository,
            sender,
            service,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PushNotificationProcessor>.Instance);

        await processor.ProcessNotificationAsync(
            Assert.Single(repository.Queued),
            CancellationToken.None);

        Assert.Single(sender.SubscriptionIds);
        Assert.Equal(currentSubscription.Id, sender.SubscriptionIds[0]);
        Assert.DoesNotContain(
            repository.Subscriptions,
            subscription => subscription.UserId == arneId);
    }

    private static Task QueueAsync(
        NotificationService service,
        NotificationType type,
        Guid userId) => type switch
    {
        NotificationType.JobAssigned => service.QueueJobAssignedAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", CancellationToken.None),
        NotificationType.JobReadyForReview => service.QueueJobReadyForReviewAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", CancellationToken.None),
        NotificationType.JobDenied => service.QueueJobDeniedAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", "Mangler dokumentation", CancellationToken.None),
        NotificationType.JobCompleted => service.QueueJobCompletedAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", CancellationToken.None),
        NotificationType.JobUnassigned => service.QueueJobUnassignedAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", CancellationToken.None),
        NotificationType.JobDeleted => service.QueueJobDeletedAsync(
            userId, "Niels Petersen", JobId, "317", "Testvej 1", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static PushSubscriptionRow CreateSubscription(Guid userId, string suffix) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Endpoint = $"https://push.example/{suffix}",
        P256Dh = "key",
        Auth = "auth",
        IsActive = true,
        CreatedUtc = DateTimeOffset.UtcNow,
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    private sealed class RecordingPushSender : IPushSender
    {
        public List<Guid> SubscriptionIds { get; } = [];
        public List<string> Payloads { get; } = [];

        public Task<PushSenderResult> SendNotificationAsync(
            PushSubscriptionRow subscription,
            string payloadJson,
            CancellationToken cancellationToken)
        {
            SubscriptionIds.Add(subscription.Id);
            Payloads.Add(payloadJson);
            return Task.FromResult(new PushSenderResult(true, null, false));
        }
    }

    private sealed class ContractRepository : INotificationRepository
    {
        public ContractRepository(Guid currentUserId, params PushSubscriptionRow[] subscriptions)
        {
            CurrentUserId = currentUserId;
            Subscriptions = subscriptions.Length == 0
                ? [CreateSubscription(currentUserId, Guid.NewGuid().ToString("N"))]
                : subscriptions.ToList();
        }

        public Guid CurrentUserId { get; }
        public List<PushSubscriptionRow> Subscriptions { get; }
        public List<NotificationQueueRow> Queued { get; } = [];
        public List<Guid> Completed { get; } = [];
        public List<NotificationDeliveryLogRow> DeliveryLogs { get; } = [];
        public HashSet<Guid> SuccessfulSubscriptionIds { get; } = [];

        public Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
        {
            Queued.Add(row);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationQueueRow>>(Queued.Take(batchSize).ToArray());

        public Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken)
        {
            Completed.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PushSubscriptionRow>>(
                Subscriptions.Where(subscription => subscription.UserId == userId && subscription.IsActive).ToArray());

        public Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(SuccessfulSubscriptionIds);

        public Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken)
        {
            var subscription = Subscriptions.Single(value => value.Id == subscriptionId);
            subscription.IsActive = isActive;
            return Task.CompletedTask;
        }

        public Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken)
        {
            DeliveryLogs.Add(log);
            if (log.Success)
            {
                SuccessfulSubscriptionIds.Add(log.SubscriptionId);
            }
            return Task.CompletedTask;
        }

        public Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, string? replacedEndpoint, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationQueueRow>>([]);

        public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
