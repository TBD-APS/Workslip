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

    public async Task QueueJobAssignedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, jobId, jobNumber, customerAddress, NotificationType.JobAssigned, cancellationToken);
    }

    public async Task QueueJobReadyForReviewAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, jobId, jobNumber, customerAddress, NotificationType.JobReadyForReview, cancellationToken);
    }

    public async Task QueueJobDeniedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, jobId, jobNumber, customerAddress, NotificationType.JobDenied, cancellationToken);
    }

    public async Task QueueJobCompletedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, jobId, jobNumber, customerAddress, NotificationType.JobCompleted, cancellationToken);
    }

    private async Task QueueNotificationInternalAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, NotificationType type, CancellationToken cancellationToken)
    {
        var url = type switch
        {
            NotificationType.JobReadyForReview or NotificationType.JobCompleted => $"/app/completed/{jobId}",
            _ => $"/app/job/{jobId}"
        };
        var payload = new NotificationPayload(jobId, jobNumber, customerAddress, type.ToString(), url);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var row = new NotificationQueueRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NotificationType = type.ToString(),
            PayloadJson = json,
            Status = "Pending",
            RetryCount = 0,
            CreatedUtc = DateTimeOffset.UtcNow,
            NextAttemptUtc = DateTimeOffset.UtcNow
        };

        await _notificationRepository.QueueNotificationAsync(row, cancellationToken);
    }

    public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress)
    {
        return notificationType switch
        {
            NotificationType.JobAssigned => (
                "Nyt job tildelt",
                $"Job #{jobNumber} er tildelt dig.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobReadyForReview => (
                "Job klar til gennemgang",
                $"Job #{jobNumber} er klar til din gennemgang.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobDenied => (
                "Job afvist",
                $"Job #{jobNumber} er blevet afvist og kræver ændringer.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobCompleted => (
                "Job afsluttet",
                $"Job #{jobNumber} er afsluttet.\nAdresse: {customerAddress}"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType), notificationType, null)
        };
    }
}
