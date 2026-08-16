using Ardalis.Result;
using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public interface INotificationService
{
    Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken);
    Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);

    Task QueueConversationMentionAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string actorName,
        Guid messageId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Conversation notifications are not implemented by this notification service.");

    Task QueueConversationActionRequestedAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string actorName,
        string actionLabel,
        Guid messageId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Conversation notifications are not implemented by this notification service.");

    Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);

    (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null);

    (string Title, string Body) GetLocalizedText(NotificationType notificationType, NotificationPayload payload) =>
        GetLocalizedText(
            notificationType,
            payload.JobNumber,
            payload.CustomerAddress,
            payload.RecipientName,
            payload.RejectionNote);
}
