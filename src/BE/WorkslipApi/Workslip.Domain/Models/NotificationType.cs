namespace Workslip.Domain.Models;

public enum NotificationType
{
    JobAssigned,
    JobReadyForReview,
    JobDenied,
    JobCompleted,
    JobUnassigned,
    JobDeleted,
    ConversationMention,
    ConversationActionRequested
}
