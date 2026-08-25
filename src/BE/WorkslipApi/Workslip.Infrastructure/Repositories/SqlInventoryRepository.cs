using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Workslip.Application.Inventory;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlInventoryRepository(ISqlConnectionFactory connectionFactory) : IInventoryRepository
{
    public async Task<IReadOnlyList<InventoryMaterialResponse>> ListMaterialsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Sku, Unit, UnitCost, IsActive, QrCode
            FROM dbo.InventoryMaterials
            WHERE OrganizationId = @OrganizationId
            ORDER BY IsActive DESC, Name ASC, Id ASC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InventoryMaterialResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<InventoryMaterialResponse> CreateMaterialAsync(
        Guid organizationId,
        CreateInventoryMaterialRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.InventoryMaterials
                (Id, OrganizationId, Name, Sku, Unit, UnitCost, IsActive, QrCode, CreatedAt)
            VALUES
                (@Id, @OrganizationId, @Name, @Sku, @Unit, @UnitCost, 1, @QrCode, @CreatedAt);
            """;

        var created = new InventoryMaterialResponse(
            Guid.NewGuid(),
            request.Name,
            request.Sku,
            request.Unit,
            request.UnitCost,
            true,
            Guid.NewGuid());

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    created.Id,
                    OrganizationId = organizationId,
                    created.Name,
                    created.Sku,
                    created.Unit,
                    created.UnitCost,
                    created.QrCode,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new InventoryDuplicateValueException("Varenummeret findes allerede i lageret.");
        }

        return created;
    }

    public async Task<IReadOnlyList<InventoryLocationResponse>> ListLocationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, IsActive
            FROM dbo.InventoryLocations
            WHERE OrganizationId = @OrganizationId
            ORDER BY IsActive DESC, Name ASC, Id ASC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InventoryLocationResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<InventoryLocationResponse> CreateLocationAsync(
        Guid organizationId,
        CreateInventoryLocationRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.InventoryLocations
                (Id, OrganizationId, Name, IsActive, CreatedAt)
            VALUES
                (@Id, @OrganizationId, @Name, 1, @CreatedAt);
            """;

        var created = new InventoryLocationResponse(Guid.NewGuid(), request.Name, true);
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    created.Id,
                    OrganizationId = organizationId,
                    created.Name,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new InventoryDuplicateValueException("Der findes allerede en lagerlokation med det navn.");
        }

        return created;
    }

    public async Task<InventoryMaterialResponse?> GetMaterialByIdAsync(
        Guid organizationId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Sku, Unit, UnitCost, IsActive, QrCode
            FROM dbo.InventoryMaterials
            WHERE OrganizationId = @OrganizationId
              AND Id = @MaterialId;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InventoryMaterialResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, MaterialId = materialId },
            cancellationToken: cancellationToken));
    }

    public async Task<InventoryMaterialResponse?> GetMaterialByQrCodeAsync(
        Guid organizationId,
        Guid qrCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Sku, Unit, UnitCost, IsActive, QrCode
            FROM dbo.InventoryMaterials
            WHERE OrganizationId = @OrganizationId
              AND QrCode = @QrCode;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InventoryMaterialResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, QrCode = qrCode },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(
        Guid organizationId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                @MaterialId AS MaterialId,
                l.Id AS LocationId,
                l.Name AS LocationName,
                COALESCE(b.Quantity, CAST(0 AS decimal(18,3))) AS Quantity
            FROM dbo.InventoryLocations l
            LEFT JOIN dbo.InventoryBalances b
                ON b.OrganizationId = l.OrganizationId
               AND b.LocationId = l.Id
               AND b.MaterialId = @MaterialId
            WHERE l.OrganizationId = @OrganizationId
              AND l.IsActive = 1
            ORDER BY l.Name ASC, l.Id ASC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InventoryBalanceResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, MaterialId = materialId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<InventoryApplyResult> ApplyMovementAsync(
        Guid organizationId,
        Guid actorUserId,
        ApplyInventoryMovementRequest request,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            var existing = await GetMovementByCommandAsync(
                connection,
                transaction,
                organizationId,
                request.CommandId,
                lockForUpdate: true,
                cancellationToken);
            if (existing is not null)
            {
                transaction.Commit();
                return new InventoryApplyResult(InventoryApplyStatus.Replay, existing);
            }

            var material = await GetLockedMaterialAsync(
                connection,
                transaction,
                organizationId,
                request.MaterialId,
                cancellationToken);
            if (material is null || !material.IsActive)
            {
                transaction.Rollback();
                return new InventoryApplyResult(InventoryApplyStatus.MaterialNotFound);
            }

            var location = await GetLockedLocationAsync(
                connection,
                transaction,
                organizationId,
                request.LocationId,
                cancellationToken);
            if (location is null || !location.IsActive)
            {
                transaction.Rollback();
                return new InventoryApplyResult(InventoryApplyStatus.LocationNotFound);
            }

            var balance = await GetLockedBalanceAsync(
                connection,
                transaction,
                organizationId,
                request.MaterialId,
                request.LocationId,
                cancellationToken);

            if (balance is null)
            {
                if (request.Direction == "out")
                {
                    transaction.Rollback();
                    return new InventoryApplyResult(InventoryApplyStatus.InsufficientStock);
                }

                const string insertBalance = """
                    INSERT INTO dbo.InventoryBalances
                        (OrganizationId, MaterialId, LocationId, Quantity, UpdatedAt)
                    VALUES
                        (@OrganizationId, @MaterialId, @LocationId, 0, @UpdatedAt);
                    """;
                await connection.ExecuteAsync(new CommandDefinition(
                    insertBalance,
                    new
                    {
                        OrganizationId = organizationId,
                        request.MaterialId,
                        request.LocationId,
                        UpdatedAt = DateTimeOffset.UtcNow
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            var now = DateTimeOffset.UtcNow;
            var signedQuantity = request.Direction == "out" ? -request.Quantity : request.Quantity;
            int changed;
            if (request.Direction == "out")
            {
                const string consume = """
                    UPDATE dbo.InventoryBalances
                    SET Quantity = Quantity - @Quantity,
                        UpdatedAt = @UpdatedAt
                    WHERE OrganizationId = @OrganizationId
                      AND MaterialId = @MaterialId
                      AND LocationId = @LocationId
                      AND Quantity >= @Quantity;
                    """;
                changed = await connection.ExecuteAsync(new CommandDefinition(
                    consume,
                    new
                    {
                        OrganizationId = organizationId,
                        request.MaterialId,
                        request.LocationId,
                        request.Quantity,
                        UpdatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
            else
            {
                const string receive = """
                    UPDATE dbo.InventoryBalances
                    SET Quantity = Quantity + @Quantity,
                        UpdatedAt = @UpdatedAt
                    WHERE OrganizationId = @OrganizationId
                      AND MaterialId = @MaterialId
                      AND LocationId = @LocationId;
                    """;
                changed = await connection.ExecuteAsync(new CommandDefinition(
                    receive,
                    new
                    {
                        OrganizationId = organizationId,
                        request.MaterialId,
                        request.LocationId,
                        request.Quantity,
                        UpdatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            if (changed != 1)
            {
                transaction.Rollback();
                return new InventoryApplyResult(InventoryApplyStatus.InsufficientStock);
            }

            const string readBalance = """
                SELECT Quantity
                FROM dbo.InventoryBalances
                WHERE OrganizationId = @OrganizationId
                  AND MaterialId = @MaterialId
                  AND LocationId = @LocationId;
                """;
            var balanceAfter = await connection.QuerySingleAsync<decimal>(new CommandDefinition(
                readBalance,
                new { OrganizationId = organizationId, request.MaterialId, request.LocationId },
                transaction,
                cancellationToken: cancellationToken));

            var movementId = Guid.NewGuid();
            const string insertMovement = """
                INSERT INTO dbo.InventoryMovements
                    (Id, OrganizationId, MaterialId, LocationId, MovementType, QuantityChange,
                     BalanceAfter, CommandId, ActorUserId, Reason, CreatedAt)
                VALUES
                    (@Id, @OrganizationId, @MaterialId, @LocationId, @MovementType, @QuantityChange,
                     @BalanceAfter, @CommandId, @ActorUserId, @Reason, @CreatedAt);
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                insertMovement,
                new
                {
                    Id = movementId,
                    OrganizationId = organizationId,
                    request.MaterialId,
                    request.LocationId,
                    MovementType = request.Direction,
                    QuantityChange = signedQuantity,
                    BalanceAfter = balanceAfter,
                    request.CommandId,
                    ActorUserId = actorUserId,
                    request.Reason,
                    CreatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));

            var actorName = await GetActorNameAsync(connection, transaction, organizationId, actorUserId, cancellationToken);
            transaction.Commit();

            return new InventoryApplyResult(
                InventoryApplyStatus.Applied,
                new InventoryMovementResponse(
                    movementId,
                    material.Id,
                    material.Name,
                    location.Id,
                    location.Name,
                    request.Direction,
                    signedQuantity,
                    balanceAfter,
                    request.CommandId,
                    actorUserId,
                    actorName,
                    request.Reason,
                    now));
        }
        catch
        {
            if (transaction.Connection is not null)
                transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<InventoryMovementResponse>> ListMovementsAsync(
        Guid organizationId,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@Limit)
                m.Id,
                m.MaterialId,
                material.Name AS MaterialName,
                m.LocationId,
                location.Name AS LocationName,
                m.MovementType,
                m.QuantityChange,
                m.BalanceAfter,
                m.CommandId,
                m.ActorUserId,
                actor.DisplayName AS ActorDisplayName,
                m.Reason,
                m.CreatedAt
            FROM dbo.InventoryMovements m
            INNER JOIN dbo.InventoryMaterials material
                ON material.OrganizationId = m.OrganizationId
               AND material.Id = m.MaterialId
            INNER JOIN dbo.InventoryLocations location
                ON location.OrganizationId = m.OrganizationId
               AND location.Id = m.LocationId
            LEFT JOIN dbo.Users actor
                ON actor.OrganizationId = m.OrganizationId
               AND actor.Id = m.ActorUserId
            WHERE m.OrganizationId = @OrganizationId
            ORDER BY m.CreatedAt DESC, m.Id DESC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InventoryMovementResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, Limit = limit },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<InventoryMovementResponse?> GetMovementByCommandAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid commandId,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var movementHint = lockForUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT
                m.Id,
                m.MaterialId,
                material.Name AS MaterialName,
                m.LocationId,
                location.Name AS LocationName,
                m.MovementType,
                m.QuantityChange,
                m.BalanceAfter,
                m.CommandId,
                m.ActorUserId,
                actor.DisplayName AS ActorDisplayName,
                m.Reason,
                m.CreatedAt
            FROM dbo.InventoryMovements m{movementHint}
            INNER JOIN dbo.InventoryMaterials material
                ON material.OrganizationId = m.OrganizationId
               AND material.Id = m.MaterialId
            INNER JOIN dbo.InventoryLocations location
                ON location.OrganizationId = m.OrganizationId
               AND location.Id = m.LocationId
            LEFT JOIN dbo.Users actor
                ON actor.OrganizationId = m.OrganizationId
               AND actor.Id = m.ActorUserId
            WHERE m.OrganizationId = @OrganizationId
              AND m.CommandId = @CommandId;
            """;

        return await connection.QuerySingleOrDefaultAsync<InventoryMovementResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, CommandId = commandId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<InventoryMaterialResponse?> GetLockedMaterialAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Sku, Unit, UnitCost, IsActive, QrCode
            FROM dbo.InventoryMaterials WITH (UPDLOCK, HOLDLOCK)
            WHERE OrganizationId = @OrganizationId
              AND Id = @MaterialId;
            """;
        return connection.QuerySingleOrDefaultAsync<InventoryMaterialResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, MaterialId = materialId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<InventoryLocationResponse?> GetLockedLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, IsActive
            FROM dbo.InventoryLocations WITH (UPDLOCK, HOLDLOCK)
            WHERE OrganizationId = @OrganizationId
              AND Id = @LocationId;
            """;
        return connection.QuerySingleOrDefaultAsync<InventoryLocationResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, LocationId = locationId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<decimal?> GetLockedBalanceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid materialId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Quantity
            FROM dbo.InventoryBalances WITH (UPDLOCK, HOLDLOCK)
            WHERE OrganizationId = @OrganizationId
              AND MaterialId = @MaterialId
              AND LocationId = @LocationId;
            """;
        return connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, MaterialId = materialId, LocationId = locationId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task<string?> GetActorNameAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DisplayName
            FROM dbo.Users
            WHERE OrganizationId = @OrganizationId
              AND Id = @ActorUserId;
            """;
        return connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, ActorUserId = actorUserId },
            transaction,
            cancellationToken: cancellationToken));
    }
}
