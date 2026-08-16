using Ardalis.Result;
using System.Security.Cryptography;
using System.Text;
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

    public Task QueueDailyHoursLimitReachedAsync(
        Guid userId,
        string recipientName,
        DateOnly workDate,
        decimal hours,
        CancellationToken cancellationToken)
    {
        var payload = new NotificationPayload(
            Guid.Empty,
            string.Empty,
            string.Empty,
            NotificationType.DailyHoursLimitReached.ToString(),
            recipientName,
            "/app/timer",
            WorkDate: workDate,
            Hours: hours);

        return QueueNotificationPayloadAsync(
            userId,
            NotificationType.DailyHoursLimitReached,
            payload,
            cancellationToken,
            CreateDailyHoursNotificationId(userId, workDate));
    }

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
            messageId: messageId);

    public async Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var deleted = await _notificationRepository.DeleteAsync(userId, notificationId, cancellationToken);
        return deleted ? Result.NoContent() : Result.NotFound();
    }

    private Task QueueNotificationInternalAsync(
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
        Guid? messageId = null)
    {
        var url = type switch
        {
            NotificationType.JobReadyForReview or NotificationType.JobCompleted => $"/app/completed/{jobId}",
            NotificationType.JobDeleted => "/app",
            NotificationType.ConversationMention or NotificationType.ConversationActionRequested =>
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
            messageId);

        return QueueNotificationPayloadAsync(userId, type, payload, cancellationToken);
    }

    private async Task QueueNotificationPayloadAsync(
        Guid userId,
        NotificationType type,
        NotificationPayload payload,
        CancellationToken cancellationToken,
        Guid? notificationId = null)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var now = DateTimeOffset.UtcNow;

        var row = new NotificationQueueRow
        {
            Id = notificationId ?? Guid.NewGuid(),
            UserId = userId,
            NotificationType = type.ToString(),
            PayloadJson = json,
            Status = "Pending",
            RetryCount = 0,
            CreatedUtc = now,
            NextAttemptUtc = now
        };

        await _notificationRepository.QueueNotificationAsync(row, cancellationToken);
    }

    private static Guid CreateDailyHoursNotificationId(Guid userId, DateOnly workDate)
    {
        var key = Encoding.UTF8.GetBytes($"daily-hours-limit:{userId:N}:{workDate:yyyyMMdd}");
        var hash = SHA256.HashData(key);
        return new Guid(hash.AsSpan(0, 16));
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
            NotificationType.DailyHoursLimitReached => (
                "Dagens maksimale timer er registreret",
                payload.WorkDate is DateOnly workDate
                    ? $"{payload.RecipientName}, du har registreret {(payload.Hours ?? 24m):0.##} timer den {workDate:dd-MM-yyyy}."
                    : $"{payload.RecipientName}, du har registreret dagens maksimale antal timer."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType), notificationType, null)
        };
    }
}
