using WebPush;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Notifications;

public sealed class WebPushSender(VapidKeyMaterial keyMaterial) : IPushSender
{
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

            using var webPushClient = new WebPushClient();
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
            var isExpired = statusCode is 404 or 410;
            return new PushSenderResult(
                false,
                $"Web Push provider returned HTTP {statusCode}.",
                isExpired);
        }
        catch (Exception)
        {
            return new PushSenderResult(
                false,
                "Unexpected Web Push provider failure.",
                false);
        }
    }
}
