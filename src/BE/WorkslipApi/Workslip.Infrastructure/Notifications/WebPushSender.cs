using Microsoft.Extensions.Logging;
using WebPush;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Notifications;

public sealed class WebPushSender(
    VapidKeyMaterial keyMaterial,
    ILogger<WebPushSender> logger) : IPushSender
{
    public async Task<PushSenderResult> SendNotificationAsync(
        PushSubscriptionRow subscription,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        // Temporary error-level trace so the existing Superadmin error dashboard
        // exposes the provider boundary without logging endpoint or key material.
        logger.LogError(
            "PUSH TRACE: Calling Web Push provider for subscription {SubscriptionId}.",
            subscription.Id);

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

            logger.LogError(
                "PUSH TRACE: Web Push provider accepted subscription {SubscriptionId}.",
                subscription.Id);
            return new PushSenderResult(true, null, false);
        }
        catch (WebPushException exception)
        {
            var statusCode = (int)exception.StatusCode;
            var isExpired = statusCode is 404 or 410;
            logger.LogError(
                "PUSH TRACE: Web Push provider rejected subscription {SubscriptionId} with HTTP {StatusCode}. Expired {IsExpired}.",
                subscription.Id,
                statusCode,
                isExpired);
            return new PushSenderResult(
                false,
                $"WebPush error (HTTP {statusCode}): {exception.Message}",
                isExpired);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "PUSH TRACE: Unexpected Web Push provider failure for subscription {SubscriptionId}.",
                subscription.Id);
            return new PushSenderResult(
                false,
                $"Unexpected error sending push notification: {exception.Message}",
                false);
        }
    }
}
