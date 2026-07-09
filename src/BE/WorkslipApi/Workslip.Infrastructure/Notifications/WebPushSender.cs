using System.Net;
using Microsoft.Extensions.Options;
using WebPush;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Configuration;

namespace Workslip.Infrastructure.Notifications;

public sealed class WebPushSender : IPushSender
{
    private readonly VapidOptions _options;

    public WebPushSender(IOptions<VapidOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PushSenderResult> SendNotificationAsync(PushSubscriptionRow subscription, string payloadJson, CancellationToken cancellationToken)
    {
        try
        {
            var pushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256Dh, subscription.Auth);
            var vapidDetails = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
            
            var webPushClient = new WebPushClient();
            await webPushClient.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails, cancellationToken);
            
            return new PushSenderResult(true, null, false);
        }
        catch (WebPushException ex)
        {
            var statusCode = (int)ex.StatusCode;
            var isExpired = statusCode == 404 || statusCode == 410;
            return new PushSenderResult(false, $"WebPush error (HTTP {statusCode}): {ex.Message}", isExpired);
        }
        catch (Exception ex)
        {
            return new PushSenderResult(false, $"Unexpected error sending push notification: {ex.Message}", false);
        }
    }
}
