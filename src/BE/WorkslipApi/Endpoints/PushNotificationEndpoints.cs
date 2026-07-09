using Workslip.Application.Auth;
using Workslip.Application.Notifications;
using ResultExtensions = Workslip.Api.Helpers.ResultExtensions;

namespace Workslip.Api.Endpoints;

public static class PushNotificationEndpoints
{
    public static IEndpointRouteBuilder MapPushNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapUserGroup("/api/push-subscriptions", "push-subscriptions");

        group.MapPost("/", async (
            RegisterPushSubscriptionRequest request,
            ICurrentUserContext currentUser,
            IPushSubscriptionService subscriptionService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var userId = currentUser.UserId;
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await subscriptionService.RegisterSubscriptionAsync(
                userId.Value,
                request.Endpoint,
                request.Keys.P256Dh,
                request.Keys.Auth,
                httpContext.Request.Headers.UserAgent.ToString(),
                cancellationToken);

            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}

public sealed record RegisterPushSubscriptionRequest(
    string Endpoint,
    PushSubscriptionKeys Keys
);

public sealed record PushSubscriptionKeys(
    string P256Dh,
    string Auth
);


