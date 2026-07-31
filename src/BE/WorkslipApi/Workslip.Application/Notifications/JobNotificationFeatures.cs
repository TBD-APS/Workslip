namespace Workslip.Application.Notifications;

public sealed class JobNotificationFeatures
{
    public const string NotifyAssignedUsersOnJobDeletionKey =
        "Features:NotifyAssignedUsersOnJobDeletion";

    public bool NotifyAssignedUsersOnJobDeletion { get; init; }
}
