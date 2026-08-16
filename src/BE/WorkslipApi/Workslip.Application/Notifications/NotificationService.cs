using Ardalis.Result;
using System.Text.Json;
using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;

    public NotificationService(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobAssigned, cancellationToken);

    public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobReadyForReview, cancellationToken);

    public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobDenied, cancellationToken, rejectionNote);

    public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobCompleted, cancellationToken);

    public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobUnassigned, cancellationToken);

    public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobDeleted, cancellationToken);

    public Task QueueConversationMentionAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string actorName,
        Guid messageId,
        CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(
            userId,
            recipientName,
            jobId,
            jobNumber,
            customerAddress,
            NotificationType.ConversationMention,
            cancellationToken,
            actorName: actorName,
            messageId: messageId);

    public Task QueueConversationActionRequestedAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string actorName,
        string actionLabel,
        Guid messageId,
        CancellationToken cancellationToken) =>
        QueueConversationActionRequestedAsync(
            userId,
            recipientName,
            jobId,
            jobNumber,
            customerAddress,
            actorName,
            actionLabel,
            messageId,
            actionType: string.Empty,
            cancellationToken);

    public Task QueueConversationActionRequestedAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string actorName,
        string actionLabel,
        Guid messageId,
        string actionType,
        CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(
            userId,
            recipientName,
            jobId,
            jobNumber,
            customerAddress,
            NotificationType.ConversationActionRequested,
            cancellationToken,
            actorName: actorName,
            actionLabel: actionLabel,
            messageId: messageId,
            actionType: string.IsNullOrWhiteSpace(actionType) ? null : actionType);

    public Task QueueConversationReminderAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        string reminderText,
        Guid messageId,
        DateTimeOffset dueUtc,
        CancellationToken cancellationToken) =>
        QueueNotificationInternalAsync(
            userId,
            recipientName,
            jobId,
            jobNumber,
            customerAddress,
            NotificationType.ConversationReminder,
            cancellationToken,
            actionLabel: reminderText,
            messageId: messageId,
            actionType: "RemindMe",
            dueUtc: dueUtc,
            nextAttemptUtc: dueUtc);

    public async Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var deleted = await _notificationRepository.DeleteAsync(userId, notificationId, cancellationToken);
        return deleted ? Result.NoContent() : Result.NotFound();
    }

    private async Task QueueNotificationInternalAsync(
        Guid userId,
        string recipientName,
        Guid jobId,
        string jobNumber,
        string customerAddress,
        NotificationType type,
        CancellationToken cancellationToken,
        string? rejectionNote = null,
        string? actorName = null,
        string? actionLabel = null,
        Guid? messageId = null,
        string? actionType = null,
        DateTimeOffset? dueUtc = null,
        DateTimeOffset? nextAttemptUtc = null)
    {
        var url = type switch
        {
            NotificationType.JobReadyForReview or NotificationType.JobCompleted => $"/app/completed/{jobId}",
            NotificationType.JobDeleted => "/app",
            NotificationType.ConversationActionRequested when string.Equals(actionType, "AssignSelf", StringComparison.Ordinal) =>
                $"/app?conversationAction={messageId}",
            NotificationType.ConversationMention or NotificationType.ConversationActionRequested or NotificationType.ConversationReminder =>
                $"/app/job/{jobId}?conversation=1{(messageId is Guid id ? $"&message={id}" : string.Empty)}",
            _ => $"/app/job/{jobId}"
        };
        var payload = new NotificationPayload(
            jobId,
            jobNumber,
            customerAddress,
            type.ToString(),
            recipientName,
            url,
            rejectionNote,
            actorName,
            actionLabel,
            messageId,
            actionType,
            dueUtc);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var now = DateTimeOffset.UtcNow;

        var row = new NotificationQueueRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NotificationType = type.ToString(),
            PayloadJson = json,
            Status = "Pending",
            RetryCount = 0,
            CreatedUtc = now,
            NextAttemptUtc = nextAttemptUtc ?? now
        };

        await _notificationRepository.QueueNotificationAsync(row, cancellationToken);
    }

    public (string Title, string Body) GetLocalizedText(
        NotificationType notificationType,
        string jobNumber,
        string customerAddress,
        string recipientName,
        string? rejectionNote = null) =>
        GetLocalizedText(
            notificationType,
            new NotificationPayload(
                Guid.Empty,
                jobNumber,
                customerAddress,
                notificationType.ToString(),
                recipientName,
                RejectionNote: rejectionNote));

    public (string Title, string Body) GetLocalizedText(NotificationType notificationType, NotificationPayload payload)
    {
        var actorName = string.IsNullOrWhiteSpace(payload.ActorName) ? "En kollega" : payload.ActorName;
        return notificationType switch
        {
            NotificationType.JobAssigned => (
                $"SAG-{payload.JobNumber} tildelt",
                $"{payload.RecipientName}, SAG-{payload.JobNumber} er tildelt dig.\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.JobReadyForReview => (
                $"SAG-{payload.JobNumber} klar til gennemgang",
                $"{payload.RecipientName}, SAG-{payload.JobNumber} er klar til din gennemgang.\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.JobDenied => (
                $"SAG-{payload.JobNumber} afvist",
                string.IsNullOrWhiteSpace(payload.RejectionNote)
                    ? $"{payload.RecipientName}, SAG-{payload.JobNumber} er blevet afvist og kræver ændringer.\nAdresse: {payload.CustomerAddress}"
                    : $"{payload.RecipientName}, SAG-{payload.JobNumber} er blevet afvist og kræver ændringer.\nÅrsag: {payload.RejectionNote}\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.JobCompleted => (
                $"SAG-{payload.JobNumber} godkendt",
                $"{payload.RecipientName}, SAG-{payload.JobNumber} er godkendt.\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.JobUnassigned => (
                "Sag uden medarbejdere",
                $"{payload.RecipientName}, SAG-{payload.JobNumber} har ingen tildelte medarbejdere.\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.JobDeleted => (
                $"SAG-{payload.JobNumber} slettet",
                $"{payload.RecipientName}, SAG-{payload.JobNumber}, som var tildelt dig, er blevet slettet.\nAdresse: {payload.CustomerAddress}"
            ),
            NotificationType.ConversationMention => (
                $"{actorName} nævnte dig · SAG-{payload.JobNumber}",
                "Du er nævnt i sagens samtale. Tryk for at åbne beskeden."
            ),
            NotificationType.ConversationActionRequested => (
                $"{actorName} beder dig handle · SAG-{payload.JobNumber}",
                string.IsNullOrWhiteSpace(payload.ActionLabel)
                    ? "Der ligger en handling til dig i sagens samtale."
                    : $"{payload.ActionLabel}. Tryk for at åbne handlingen."
            ),
            NotificationType.ConversationReminder => (
                $"Påmindelse · SAG-{payload.JobNumber}",
                string.IsNullOrWhiteSpace(payload.ActionLabel)
                    ? "Du bad Workslip om at minde dig om denne sag."
                    : payload.ActionLabel
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType), notificationType, null)
        };
    }
}
