namespace Workslip.Application.Notifications;

public interface IPushSubscriptionService
{
    Task<Ardalis.Result.Result> RegisterSubscriptionAsync(
        Guid userId,
        string endpoint,
        string p256Dh,
        string auth,
        string? userAgent,
        string? replacedEndpoint,
        CancellationToken cancellationToken);
}
