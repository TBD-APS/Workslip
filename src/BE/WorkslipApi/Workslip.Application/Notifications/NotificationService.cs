using System.Text.Json;
using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserContext _currentUser;

    public NotificationService(INotificationRepository notificationRepository, ICurrentUserContext currentUser)
    {
        _notificationRepository = notificationRepository;
        _currentUser = currentUser;
    }

    public async Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobAssigned, cancellationToken);
    }

    public async Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobReadyForReview, cancellationToken);
    }

    public async Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobDenied, cancellationToken);
    }

    public async Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobCompleted, cancellationToken);
    }

    public async Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
    {
        await QueueNotificationInternalAsync(userId, recipientName, jobId, jobNumber, customerAddress, NotificationType.JobUnassigned, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Result.Unauthorized();
        }

        var deleted = await _notificationRepository.DeleteAsync(userId.Value, notificationId, cancellationToken);
        return deleted ? Result.Success() : Result.NotFound();
    }

    private async Task QueueNotificationInternalAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, NotificationType type, CancellationToken cancellationToken)
    {
        var url = type switch
        {
            NotificationType.JobReadyForReview or NotificationType.JobCompleted => $"/app/completed/{jobId}",
            _ => $"/app/job/{jobId}"
        };
        var payload = new NotificationPayload(jobId, jobNumber, customerAddress, type.ToString(), recipientName, url);
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

    public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName)
    {
        return notificationType switch
        {
            NotificationType.JobAssigned => (
                $"SAG-{jobNumber} tildelt",
                $"{recipientName}, SAG-{jobNumber} er tildelt dig.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobReadyForReview => (
                $"SAG-{jobNumber} klar til gennemgang",
                $"{recipientName}, SAG-{jobNumber} er klar til din gennemgang.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobDenied => (
                $"SAG-{jobNumber} afvist",
                $"{recipientName}, SAG-{jobNumber} er blevet afvist og kræver ændringer.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobCompleted => (
                $"SAG-{jobNumber} afsluttet",
                $"{recipientName}, SAG-{jobNumber} er afsluttet.\nAdresse: {customerAddress}"
            ),
            NotificationType.JobUnassigned => (
                "Sag uden medarbejdere",
                $"{recipientName}, SAG-{jobNumber} har ingen tildelte medarbejdere.\nAdresse: {customerAddress}"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(notificationType), notificationType, null)
        };
    }
}
