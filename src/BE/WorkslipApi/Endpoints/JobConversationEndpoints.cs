using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Application.Auth;
using Workslip.Application.Conversations;

namespace Workslip.Api.Endpoints;

public static class JobConversationEndpoints
{
    public static IEndpointRouteBuilder MapJobConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapUserGroup("/api/jobs", "job-conversations");

        group.MapGet("/{jobId:guid}/conversation", async (
            Guid jobId,
            int? limit,
            int? offset,
            HttpContext httpContext,
            IJobConversationService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(jobId, limit, offset, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<JobConversationResponse>(StatusCodes.Status200OK);

        group.MapPost("/{jobId:guid}/conversation/messages", async (
            Guid jobId,
            CreateConversationMessageRequest request,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            IdempotencyStore idempotency,
            IJobConversationService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

            var reservation = await idempotency.StartAsync(
                $"job-conversation.message:{currentUser.OrganizationId}:{currentUser.UserId}:{jobId}",
                key,
                request,
                cancellationToken);
            var replay = IdempotencyHttp.ReplayOrReject(reservation);
            if (replay is not null) return replay;

            try
            {
                var result = await service.SendAsync(jobId, request, cancellationToken);
                if (result.IsSuccess)
                {
                    await idempotency.CompleteAsync(
                        reservation.Reservation!.Id,
                        reservation.ReservationToken!,
                        result.Value,
                        StatusCodes.Status200OK,
                        cancellationToken);
                }
                else
                {
                    await idempotency.AbortAsync(
                        reservation.Reservation!.Id,
                        reservation.ReservationToken!,
                        cancellationToken);
                }

                return ResultExtensions.ToHttpResult(result);
            }
            catch
            {
                await idempotency.AbortAsync(
                    reservation.Reservation!.Id,
                    reservation.ReservationToken!,
                    CancellationToken.None);
                throw;
            }
        }).Produces<ConversationMessageResponse>(StatusCodes.Status200OK);

        group.MapPost("/{jobId:guid}/conversation/messages/{messageId:guid}/resolve", async (
            Guid jobId,
            Guid messageId,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            IdempotencyStore idempotency,
            IJobConversationService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

            var request = new { jobId, messageId };
            var reservation = await idempotency.StartAsync(
                $"job-conversation.resolve:{currentUser.OrganizationId}:{currentUser.UserId}:{jobId}:{messageId}",
                key,
                request,
                cancellationToken);
            var replay = IdempotencyHttp.ReplayOrReject(reservation);
            if (replay is not null) return replay;

            try
            {
                var result = await service.ResolveActionAsync(jobId, messageId, cancellationToken);
                if (result.IsSuccess)
                {
                    await idempotency.CompleteAsync(
                        reservation.Reservation!.Id,
                        reservation.ReservationToken!,
                        result.Value,
                        StatusCodes.Status200OK,
                        cancellationToken);
                }
                else
                {
                    await idempotency.AbortAsync(
                        reservation.Reservation!.Id,
                        reservation.ReservationToken!,
                        cancellationToken);
                }

                return ResultExtensions.ToHttpResult(result);
            }
            catch
            {
                await idempotency.AbortAsync(
                    reservation.Reservation!.Id,
                    reservation.ReservationToken!,
                    CancellationToken.None);
                throw;
            }
        }).Produces<ConversationMessageResponse>(StatusCodes.Status200OK);

        group.MapPost("/{jobId:guid}/conversation/read", async (
            Guid jobId,
            HttpContext httpContext,
            IJobConversationService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.MarkReadAsync(jobId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
