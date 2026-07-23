using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Services;

public sealed record IdempotencyStartResult(
    IdempotencyRecordRow? Reservation,
    string? ReservationToken,
    string? ResponseJson,
    int? StatusCode,
    bool RequestHashConflict,
    bool InProgress)
{
    public bool IsReplay => ResponseJson is not null && StatusCode is not null;
}

public sealed class IdempotencyStore(SqlDbContext db)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdempotencyStartResult> StartAsync(
        string scope,
        string key,
        object request,
        CancellationToken cancellationToken)
    {
        var requestHash = Hash(request);
        var now = DateTimeOffset.UtcNow;

        var expired = await db.IdempotencyRecords
            .Where(x => x.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            db.IdempotencyRecords.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
        }

        var existing = await db.IdempotencyRecords
            .SingleOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                return new(null, null, null, null, true, false);

            if (existing.Completed && existing.ResponseJson is not null)
                return new(null, null, existing.ResponseJson, existing.StatusCode, false, false);

            if (existing.CreatedAt.Add(Lease) > now)
                return new(null, null, null, null, false, true);

            existing.CreatedAt = now;
            existing.ExpiresAt = now.Add(Lease);
            existing.ReservationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await db.SaveChangesAsync(cancellationToken);
            return new(existing, existing.ReservationToken, null, null, false, false);
        }

        var reservation = new IdempotencyRecordRow
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            Key = key,
            RequestHash = requestHash,
            ReservationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            CreatedAt = now,
            ExpiresAt = now.Add(Lease),
        };

        db.IdempotencyRecords.Add(reservation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(reservation, reservation.ReservationToken, null, null, false, false);
        }
        catch (DbUpdateException)
        {
            db.Entry(reservation).State = EntityState.Detached;
            var raced = await db.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync(x => x.Scope == scope && x.Key == key, cancellationToken);

            if (!string.Equals(raced.RequestHash, requestHash, StringComparison.Ordinal))
                return new(null, null, null, null, true, false);
            if (raced.Completed && raced.ResponseJson is not null)
                return new(null, null, raced.ResponseJson, raced.StatusCode, false, false);
            return new(null, null, null, null, false, true);
        }
    }

    public async Task CompleteAsync(Guid reservationId, string reservationToken, object response, int statusCode, CancellationToken cancellationToken)
    {
        var reservation = await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.Id == reservationId && x.ReservationToken == reservationToken, cancellationToken);
        if (reservation is null) return;
        reservation.Completed = true;
        reservation.StatusCode = statusCode;
        reservation.ResponseJson = JsonSerializer.Serialize(response, JsonOptions);
        reservation.ExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AbortAsync(Guid reservationId, string reservationToken, CancellationToken cancellationToken)
    {
        var reservation = await db.IdempotencyRecords
            .SingleOrDefaultAsync(x => x.Id == reservationId && x.ReservationToken == reservationToken, cancellationToken);
        if (reservation is null) return;
        db.IdempotencyRecords.Remove(reservation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static string Hash(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
