using WebPush;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Notifications;

public sealed class WebPushSender(
    VapidKeyMaterial keyMaterial,
    IWebPushClient webPushClient) : IPushSender
{
    private const string VapidPublicKeyMismatchReason = "VapidPkHashMismatch";

    public async Task<PushSenderResult> SendNotificationAsync(
        PushSubscriptionRow subscription,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var pushSubscription = new WebPush.PushSubscription(
                subscription.Endpoint,
                subscription.P256Dh,
                subscription.Auth);
            var vapidDetails = new VapidDetails(
                keyMaterial.Subject,
                keyMaterial.PublicKey,
                keyMaterial.PrivateKey);

            await webPushClient.SendNotificationAsync(
                pushSubscription,
                payloadJson,
                vapidDetails,
                cancellationToken);

            return new PushSenderResult(true, null, false);
        }
        catch (WebPushException exception)
        {
            var statusCode = (int)exception.StatusCode;
            var shouldDeactivateSubscription = statusCode is 404 or 410
                || statusCode == 400 && exception.Message.Contains(
                    VapidPublicKeyMismatchReason,
                    StringComparison.OrdinalIgnoreCase);

            return new PushSenderResult(
                false,
                $"WebPush error (HTTP {statusCode}): {exception.Message}",
                shouldDeactivateSubscription);
        }
        catch (Exception exception)
        {
            return new PushSenderResult(
                false,
                $"Unexpected error sending push notification: {exception.Message}",
                false);
        }
    }
}
