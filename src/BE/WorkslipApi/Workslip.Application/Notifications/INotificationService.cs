using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public interface INotificationService
{
    Task QueueJobAssignedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobReadyForReviewAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobDeniedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    Task QueueJobCompletedAsync(Guid userId, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken);
    
    (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress);
}
