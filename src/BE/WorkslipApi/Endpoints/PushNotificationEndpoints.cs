using Workslip.Application.Auth;
using Workslip.Application.Notifications;
using System.Text.Json;
using Workslip.Domain.Models;
using ResultExtensions = Workslip.Api.Helpers.ResultExtensions;

namespace Workslip.Api.Endpoints;

public static class PushNotificationEndpoints
{
    public static IEndpointRouteBuilder MapPushNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapUserGroup("/api/push-subscriptions", "push-subscriptions");

        var notifications = app.MapUserGroup("/api/notifications", "notifications");
        notifications.MapGet("/", async (int? limit, int? offset, ICurrentUserContext currentUser, INotificationRepository repository, INotificationService service, CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is null) return Results.Unauthorized();
            var rows = await repository.GetHistoryAsync(currentUser.UserId.Value, Math.Clamp(limit ?? 50, 1, 100), Math.Max(offset ?? 0, 0), cancellationToken);
            var result = rows.Select(row =>
            {
                var payload = JsonSerializer.Deserialize<NotificationPayload>(row.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var type = Enum.TryParse<NotificationType>(row.NotificationType, out var parsed) ? parsed : (NotificationType?)null;
                var text = payload is not null && type is not null ? service.GetLocalizedText(type.Value, payload.JobNumber, payload.CustomerAddress, payload.RecipientName) : ("Notifikation", "Du har modtaget en ny notifikation.");
                return new NotificationHistoryViewModel(row.Id, text.Item1, text.Item2, payload?.Url, row.CreatedUtc, row.ReadUtc is not null, row.Status);
            }).ToArray();
            return Results.Ok(result);
        });
        notifications.MapPatch("/{id:guid}/read", async (Guid id, ICurrentUserContext currentUser, INotificationRepository repository, CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is null) return Results.Unauthorized();
            await repository.MarkReadAsync(currentUser.UserId.Value, id, cancellationToken);
            return Results.NoContent();
        });
        notifications.MapPost("/read-all", async (ICurrentUserContext currentUser, INotificationRepository repository, CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is null) return Results.Unauthorized();
            await repository.MarkAllReadAsync(currentUser.UserId.Value, cancellationToken);
            return Results.NoContent();
        });
        notifications.MapDelete("/{id:guid}", async (Guid id, ICurrentUserContext currentUser, INotificationService service, CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is null) return Results.Unauthorized();
            var result = await service.DeleteAsync(currentUser.UserId.Value, id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

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

public sealed record NotificationHistoryViewModel(Guid Id, string Title, string Body, string? Url, DateTimeOffset CreatedUtc, bool IsRead, string Status);

public sealed record RegisterPushSubscriptionRequest(
    string Endpoint,
    PushSubscriptionKeys Keys
);

public sealed record PushSubscriptionKeys(
    string P256Dh,
    string Auth
);
