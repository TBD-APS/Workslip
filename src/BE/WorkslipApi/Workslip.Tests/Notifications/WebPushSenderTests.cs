using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebPush;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Notifications;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class WebPushSenderTests
{
    private const string PrivateScalarOne =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAE";

    [Fact]
    public async Task SendNotificationAsync_DeactivatesSubscriptionForVapidPublicKeyMismatch()
    {
        using var client = new StubWebPushClient((subscription, _, _, _) =>
            throw CreateWebPushException(
                subscription,
                HttpStatusCode.BadRequest,
                "{\"reason\":\"VapidPkHashMismatch\"}"));
        var sender = CreateSender(client);

        var result = await sender.SendNotificationAsync(
            CreateSubscription(),
            "{}",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.ShouldDeactivateSubscription);
        Assert.Contains("VapidPkHashMismatch", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendNotificationAsync_DoesNotDeactivateSubscriptionForOtherBadRequests()
    {
        using var client = new StubWebPushClient((subscription, _, _, _) =>
            throw CreateWebPushException(
                subscription,
                HttpStatusCode.BadRequest,
                "{\"reason\":\"InvalidRequest\"}"));
        var sender = CreateSender(client);

        var result = await sender.SendNotificationAsync(
            CreateSubscription(),
            "{}",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ShouldDeactivateSubscription);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SendNotificationAsync_PreservesExpiredSubscriptionCleanup(HttpStatusCode statusCode)
    {
        using var client = new StubWebPushClient((subscription, _, _, _) =>
            throw CreateWebPushException(subscription, statusCode, null));
        var sender = CreateSender(client);

        var result = await sender.SendNotificationAsync(
            CreateSubscription(),
            "{}",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.ShouldDeactivateSubscription);
    }

    private static WebPushSender CreateSender(IWebPushClient client)
    {
        var keyMaterial = new VapidKeyMaterial(
            Options.Create(new VapidOptions
            {
                PrivateKey = PrivateScalarOne,
                Subject = "mailto:push@workslip.app"
            }),
            NullLogger<VapidKeyMaterial>.Instance);

        return new WebPushSender(keyMaterial, client);
    }

    private static PushSubscriptionRow CreateSubscription() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Endpoint = "https://push.example.test/subscription",
        P256Dh = "p256dh",
        Auth = "auth"
    };

    private static WebPushException CreateWebPushException(
        PushSubscription subscription,
        HttpStatusCode statusCode,
        string? details)
    {
        var response = new HttpResponseMessage(statusCode);
        if (details is not null)
        {
            response.Content = new StringContent(details);
        }

        var message = details is null
            ? statusCode.ToString()
            : $"Bad Request. Details: {details}";
        return new WebPushException(message, subscription, response);
    }

    private sealed class StubWebPushClient(
        Func<PushSubscription, string, VapidDetails, CancellationToken, Task> send) : IWebPushClient
    {
        public Task SendNotificationAsync(
            PushSubscription subscription,
            string payload,
            VapidDetails vapidDetails,
            CancellationToken cancellationToken = default) =>
            send(subscription, payload, vapidDetails, cancellationToken);

        public void Dispose()
        {
        }

        public void SetGcmApiKey(string gcmApiKey) => throw new NotSupportedException();

        public void SetVapidDetails(VapidDetails vapidDetails) => throw new NotSupportedException();

        public void SetVapidDetails(string subject, string publicKey, string privateKey) =>
            throw new NotSupportedException();

        public HttpRequestMessage GenerateRequestDetails(
            PushSubscription subscription,
            string payload,
            Dictionary<string, object> options) =>
            throw new NotSupportedException();

        public void SendNotification(
            PushSubscription subscription,
            string payload,
            Dictionary<string, object> options) =>
            throw new NotSupportedException();

        public void SendNotification(
            PushSubscription subscription,
            string payload,
            VapidDetails vapidDetails) =>
            throw new NotSupportedException();

        public void SendNotification(
            PushSubscription subscription,
            string payload,
            string gcmApiKey) =>
            throw new NotSupportedException();

        public Task SendNotificationAsync(
            PushSubscription subscription,
            string payload,
            Dictionary<string, object> options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendNotificationAsync(
            PushSubscription subscription,
            string payload,
            string gcmApiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
