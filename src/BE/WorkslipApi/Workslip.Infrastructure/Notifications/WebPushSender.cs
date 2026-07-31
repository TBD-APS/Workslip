using System.Text.Json;
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
                || statusCode == 400
                && await HasVapidPublicKeyMismatchReasonAsync(exception, cancellationToken);

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

    private static async Task<bool> HasVapidPublicKeyMismatchReasonAsync(
        WebPushException exception,
        CancellationToken cancellationToken)
    {
        if (exception.HttpResponseMessage.Content is null)
        {
            return false;
        }

        var details = await exception.HttpResponseMessage.Content
            .ReadAsStringAsync(cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(details);
            return document.RootElement.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String
                && string.Equals(
                    reason.GetString(),
                    VapidPublicKeyMismatchReason,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
