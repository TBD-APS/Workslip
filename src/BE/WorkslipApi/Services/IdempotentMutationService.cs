using Ardalis.Result;

namespace Workslip.Api.Services;

public sealed record IdempotentMutationResult<T>(Result<T>? Result, string? ReplayJson, int? ReplayStatusCode, bool Conflict, bool InProgress)
{
    public bool IsReplay => ReplayJson is not null && ReplayStatusCode is not null;
}

public sealed class IdempotentMutationService(IdempotencyStore store)
{
    public async Task<IdempotentMutationResult<T>> ExecuteAsync<TRequest, T>(
        string scope,
        string key,
        TRequest request,
        Func<Task<Result<T>>> operation,
        Func<T, object> responseMapper,
        CancellationToken cancellationToken)
    {
        var reservation = await store.StartAsync(scope, key, request!, cancellationToken);
        if (reservation.RequestHashConflict) return new(null, null, null, true, false);
        if (reservation.InProgress) return new(null, null, null, false, true);
        if (reservation.IsReplay) return new(null, reservation.ResponseJson, reservation.StatusCode, false, false);

        try
        {
            var result = await operation();
            if (result.IsSuccess)
                await store.CompleteAsync(reservation.Reservation!.Id, reservation.ReservationToken!, responseMapper(result.Value), StatusCodes.Status200OK, cancellationToken);
            else
                await store.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, cancellationToken);
            return new(result, null, null, false, false);
        }
        catch
        {
            await store.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, CancellationToken.None);
            throw;
        }
    }
}
