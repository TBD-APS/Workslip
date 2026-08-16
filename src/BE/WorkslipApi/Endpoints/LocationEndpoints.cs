using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints;

public static class LocationEndpoints
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var user = app.MapUserGroup("/api/location", "location");
        var admin = app.MapAdminGroup("/api/location", "location");

        user.MapPost("/sessions/start", StartSessionAsync);
        user.MapPost("/pings", PingAsync);
        user.MapPost("/sessions/{id:guid}/stop", StopSessionAsync);
        user.MapGet("/me", GetMeAsync);
        admin.MapGet("/current", GetCurrentAsync);

        return app;
    }

    private static async Task<IResult> StartSessionAsync(
        SqlDbContext db,
        ICurrentUserContext currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);
        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var activeSessionId = await FindActiveSessionAsync(connection, transaction, organizationId, userId, cancellationToken);
        if (activeSessionId is Guid existingId)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new TrackingSessionResponse(existingId, true, null));
        }

        var sessionId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dbo.LocationTrackingSessions
                (Id, OrganizationId, UserId, StartedAt, Source, Status)
            VALUES
                (@id, @organizationId, @userId, sysutcdatetime(), N'Phone', N'Active');
            """;
        command.Parameters.AddWithValue("@id", sessionId);
        command.Parameters.AddWithValue("@organizationId", organizationId);
        command.Parameters.AddWithValue("@userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new TrackingSessionResponse(sessionId, true, null));
    }

    private static async Task<IResult> PingAsync(
        LocationPingRequest request,
        SqlDbContext db,
        ICurrentUserContext currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);
        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 || request.AccuracyMeters is < 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["location"] = ["Koordinater eller accuracy er ugyldige."]
            });

        var capturedAt = request.CapturedAt == default ? DateTimeOffset.UtcNow : request.CapturedAt.ToUniversalTime();
        if (capturedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["capturedAt"] = ["Tidspunktet ligger for langt i fremtiden."] });

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var ownership = connection.CreateCommand();
        ownership.Transaction = transaction;
        ownership.CommandText = """
            SELECT COUNT(1)
            FROM dbo.LocationTrackingSessions WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = @sessionId
              AND OrganizationId = @organizationId
              AND UserId = @userId
              AND Status = N'Active';
            """;
        ownership.Parameters.AddWithValue("@sessionId", request.SessionId);
        ownership.Parameters.AddWithValue("@organizationId", organizationId);
        ownership.Parameters.AddWithValue("@userId", userId);
        var ownsActiveSession = Convert.ToInt32(await ownership.ExecuteScalarAsync(cancellationToken)) == 1;
        if (!ownsActiveSession)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Results.Conflict(new { error = "tracking_session_not_active" });
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            MERGE dbo.EmployeeLastLocations WITH (HOLDLOCK) AS target
            USING (SELECT @organizationId AS OrganizationId, @userId AS UserId) AS source
              ON target.OrganizationId = source.OrganizationId AND target.UserId = source.UserId
            WHEN MATCHED AND @capturedAt >= target.CapturedAt THEN
                UPDATE SET
                    SessionId = @sessionId,
                    CapturedAt = @capturedAt,
                    Latitude = @latitude,
                    Longitude = @longitude,
                    AccuracyMeters = @accuracyMeters,
                    UpdatedAt = sysutcdatetime()
            WHEN NOT MATCHED THEN
                INSERT (OrganizationId, UserId, SessionId, CapturedAt, Latitude, Longitude, AccuracyMeters, UpdatedAt)
                VALUES (@organizationId, @userId, @sessionId, @capturedAt, @latitude, @longitude, @accuracyMeters, sysutcdatetime());
            """;
        command.Parameters.AddWithValue("@organizationId", organizationId);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@sessionId", request.SessionId);
        command.Parameters.AddWithValue("@capturedAt", capturedAt);
        command.Parameters.AddWithValue("@latitude", request.Latitude);
        command.Parameters.AddWithValue("@longitude", request.Longitude);
        command.Parameters.AddWithValue("@accuracyMeters", (object?)request.AccuracyMeters ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new { capturedAt });
    }

    private static async Task<IResult> StopSessionAsync(
        Guid id,
        SqlDbContext db,
        ICurrentUserContext currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);
        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.LocationTrackingSessions
            SET Status = N'Stopped', EndedAt = COALESCE(EndedAt, sysutcdatetime())
            WHERE Id = @id AND OrganizationId = @organizationId AND UserId = @userId AND Status = N'Active';
            SELECT @@ROWCOUNT;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@organizationId", organizationId);
        command.Parameters.AddWithValue("@userId", userId);
        var affected = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return affected == 1 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetMeAsync(
        SqlDbContext db,
        ICurrentUserContext currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);
        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1)
                s.Id, s.Status, l.CapturedAt, l.Latitude, l.Longitude, l.AccuracyMeters
            FROM dbo.LocationTrackingSessions s
            LEFT JOIN dbo.EmployeeLastLocations l
              ON l.OrganizationId = s.OrganizationId AND l.UserId = s.UserId AND l.SessionId = s.Id
            WHERE s.OrganizationId = @organizationId AND s.UserId = @userId
            ORDER BY CASE WHEN s.Status = N'Active' THEN 0 ELSE 1 END, s.StartedAt DESC;
            """;
        command.Parameters.AddWithValue("@organizationId", organizationId);
        command.Parameters.AddWithValue("@userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Results.Ok(new MyTrackingStatusResponse(null, false, null, null, null, null));

        return Results.Ok(new MyTrackingStatusResponse(
            reader.GetGuid(0),
            reader.GetString(1) == "Active",
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5)));
    }

    private static async Task<IResult> GetCurrentAsync(
        SqlDbContext db,
        ICurrentUserContext currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);
        if (currentUser.OrganizationId is not Guid organizationId)
            return Results.Unauthorized();

        var connection = (SqlConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.Id,
                u.DisplayName,
                l.SessionId,
                l.CapturedAt,
                l.Latitude,
                l.Longitude,
                l.AccuracyMeters,
                CASE WHEN s.Status = N'Active' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            FROM dbo.EmployeeLastLocations l
            INNER JOIN dbo.Users u
              ON u.OrganizationId = l.OrganizationId AND u.Id = l.UserId
            INNER JOIN dbo.LocationTrackingSessions s
              ON s.Id = l.SessionId AND s.OrganizationId = l.OrganizationId AND s.UserId = l.UserId
            WHERE l.OrganizationId = @organizationId
            ORDER BY l.CapturedAt DESC;
            """;
        command.Parameters.AddWithValue("@organizationId", organizationId);

        var now = DateTimeOffset.UtcNow;
        var rows = new List<CurrentEmployeeLocationResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var capturedAt = reader.GetFieldValue<DateTimeOffset>(3);
            var ageSeconds = Math.Max(0, (int)(now - capturedAt).TotalSeconds);
            rows.Add(new CurrentEmployeeLocationResponse(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? "Ukendt medarbejder" : reader.GetString(1),
                reader.GetGuid(2),
                capturedAt,
                ageSeconds,
                ageSeconds >= StaleAfter.TotalSeconds,
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetBoolean(7)));
        }

        return Results.Ok(rows);
    }

    private static async Task<Guid?> FindActiveSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP (1) Id
            FROM dbo.LocationTrackingSessions WITH (UPDLOCK, HOLDLOCK)
            WHERE OrganizationId = @organizationId AND UserId = @userId AND Status = N'Active'
            ORDER BY StartedAt DESC;
            """;
        command.Parameters.AddWithValue("@organizationId", organizationId);
        command.Parameters.AddWithValue("@userId", userId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task EnsureOpenAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }

    public sealed record LocationPingRequest(
        Guid SessionId,
        decimal Latitude,
        decimal Longitude,
        decimal? AccuracyMeters,
        DateTimeOffset CapturedAt);

    public sealed record TrackingSessionResponse(Guid SessionId, bool Active, DateTimeOffset? EndedAt);

    public sealed record MyTrackingStatusResponse(
        Guid? SessionId,
        bool Active,
        DateTimeOffset? CapturedAt,
        decimal? Latitude,
        decimal? Longitude,
        decimal? AccuracyMeters);

    public sealed record CurrentEmployeeLocationResponse(
        Guid UserId,
        string DisplayName,
        Guid SessionId,
        DateTimeOffset CapturedAt,
        int AgeSeconds,
        bool IsStale,
        decimal Latitude,
        decimal Longitude,
        decimal? AccuracyMeters,
        bool TrackingActive);
}