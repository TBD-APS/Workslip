using Result = Ardalis.Result.Result;
using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public sealed class PushSubscriptionService(INotificationRepository notificationRepository) : IPushSubscriptionService
{
    public async Task<Result> RegisterSubscriptionAsync(
        Guid userId,
        string endpoint,
        string p256Dh,
        string auth,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || 
            string.IsNullOrWhiteSpace(p256Dh) || 
            string.IsNullOrWhiteSpace(auth))
        {
            return Result.Invalid(new Ardalis.Result.ValidationError("Endpoint, P256Dh, and Auth keys are required."));
        }

        await notificationRepository.RegisterSubscriptionAsync(
            userId,
            endpoint,
            p256Dh,
            auth,
            userAgent,
            cancellationToken);

        return Result.Success();
    }
}
