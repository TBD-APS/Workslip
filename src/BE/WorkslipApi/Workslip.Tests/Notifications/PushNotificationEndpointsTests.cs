using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Workslip.Api.Endpoints;
using Workslip.Application.Auth;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class PushNotificationEndpointsTests
{
    [Fact]
    public async Task RegisterSubscriptionAsync_AllowsSuperadminActor()
    {
        var userId = Guid.NewGuid();
        var currentUser = new TestCurrentUserContext(userId, Guid.NewGuid(), Roles.Superadmin);
        var service = new RecordingPushSubscriptionService();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = "Workslip test";
        var request = new RegisterPushSubscriptionRequest(
            "https://push.example/superadmin",
            new PushSubscriptionKeys("p256dh", "auth"));

        await PushNotificationEndpoints.RegisterSubscriptionAsync(
            request,
            currentUser,
            service,
            httpContext,
            CancellationToken.None);

        Assert.True(service.WasCalled);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(request.Endpoint, service.Endpoint);
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingPushSubscriptionService : IPushSubscriptionService
    {
        public bool WasCalled { get; private set; }
        public Guid UserId { get; private set; }
        public string? Endpoint { get; private set; }

        public Task<Result> RegisterSubscriptionAsync(
            Guid userId,
            string endpoint,
            string p256Dh,
            string auth,
            string? userAgent,
            string? replacedEndpoint,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            Endpoint = endpoint;
            return Task.FromResult(Result.Success());
        }
    }
}
