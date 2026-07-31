using Microsoft.Extensions.Logging;
using Workslip.Application.Jobs;

namespace Workslip.Application.Notifications;

public sealed class JobDeletionNotificationService(
    INotificationService notificationService,
    ILogger<JobDeletionNotificationService> logger)
{
    public bool IsEnabled => true;

    public async Task QueueAsync(
        JobReportResponse deletedJob,
        Guid? deletingUserId,
        CancellationToken cancellationToken)
    {
        var reportNumber = deletedJob.ReportNumber ?? "Uden nummer";
        var address = deletedJob.DestinationAddress
            ?? deletedJob.Customer?.Address
            ?? "Ingen adresse angivet";

        var recipients = deletedJob.AssignedUsers
            .GroupBy(user => user.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (var recipient in recipients)
        {
            try
            {
                await notificationService.QueueJobDeletedAsync(
                    recipient.Id,
                    recipient.DisplayName,
                    deletedJob.Id,
                    reportNumber,
                    address,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to queue job deletion notification. JobId: {JobId}. UserId: {UserId}.",
                    deletedJob.Id,
                    recipient.Id);
            }
        }
    }
}
