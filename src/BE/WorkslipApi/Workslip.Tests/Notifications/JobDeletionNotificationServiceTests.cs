using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class JobDeletionNotificationServiceTests
{
    [Fact]
    public async Task QueueAsync_NotifiesEveryUniqueAssigneeIncludingTheDeletingUser()
    {
        var deletingUserId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var secondRecipientId = Guid.NewGuid();
        var notifications = new RecordingNotificationService();
        var service = CreateService(notifications);

        await service.QueueAsync(
            CreateJob([
                new AssignedUserResponse(deletingUserId, "Slettende administrator"),
                new AssignedUserResponse(recipientId, "Første montør"),
                new AssignedUserResponse(recipientId, "Duplikat"),
                new AssignedUserResponse(secondRecipientId, "Anden montør")
            ]),
            deletingUserId,
            CancellationToken.None);

        Assert.Equal(
            new[] { deletingUserId, recipientId, secondRecipientId },
            notifications.DeletedRecipients);
    }

    [Fact]
    public async Task QueueAsync_ContinuesWhenOneRecipientCannotBeQueued()
    {
        var failingRecipientId = Guid.NewGuid();
        var successfulRecipientId = Guid.NewGuid();
        var notifications = new RecordingNotificationService(failingRecipientId);
        var service = CreateService(notifications);

        await service.QueueAsync(
            CreateJob([
                new AssignedUserResponse(failingRecipientId, "Fejlende modtager"),
                new AssignedUserResponse(successfulRecipientId, "Gyldig modtager")
            ]),
            deletingUserId: null,
            CancellationToken.None);

        Assert.Equal(new[] { failingRecipientId, successfulRecipientId }, notifications.DeletedRecipients);
    }

    private static JobDeletionNotificationService CreateService(
        RecordingNotificationService notifications) =>
        new(
            notifications,
            NullLogger<JobDeletionNotificationService>.Instance);

    private static JobReportResponse CreateJob(IReadOnlyList<AssignedUserResponse> assignedUsers) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Testorganisation",
            "12345678",
            new CustomerInfo(null, "Kunde", "Kundeadresse 1", null, null, null),
            "0042",
            "Arbejdsadresse 2",
            "8000",
            "Aarhus C",
            JobStatus.Draft,
            null,
            JobType.KLS,
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
            null,
            assignedUsers,
            [],
            false,
            null,
            null,
            null);

    private sealed class RecordingNotificationService(Guid? failingRecipientId = null) : INotificationService
    {
        internal List<Guid> DeletedRecipients { get; } = [];

        public Task QueueJobDeletedAsync(
            Guid userId,
            string recipientName,
            Guid jobId,
            string jobNumber,
            string customerAddress,
            CancellationToken cancellationToken)
        {
            DeletedRecipients.Add(userId);
            return userId == failingRecipientId
                ? Task.FromException(new InvalidOperationException("queue failure"))
                : Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.NoContent());

        public (string Title, string Body) GetLocalizedText(
            NotificationType notificationType,
            string jobNumber,
            string customerAddress,
            string recipientName,
            string? rejectionNote = null) =>
            throw new NotSupportedException();
    }
}
