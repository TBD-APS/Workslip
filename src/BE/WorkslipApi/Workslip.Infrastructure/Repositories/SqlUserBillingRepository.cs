using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Users;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlUserBillingRepository(SqlDbContext dbContext) : IUserBillingRepository
{
    public async Task<decimal?> GetRateAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var table = TableName("UserBillingRates");
            var command = new CommandDefinition(
                $"SELECT BillableHourlyRate FROM {table} WHERE OrganizationId = @OrganizationId AND UserId = @UserId",
                new { OrganizationId = organizationId, UserId = userId },
                transaction,
                cancellationToken: cancellationToken);

            return await connection.QuerySingleOrDefaultAsync<decimal?>(command);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task SetRateAsync(
        Guid organizationId,
        Guid userId,
        decimal? rate,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var table = TableName("UserBillingRates");
            var sql = IsSqlServer
                ? $"""
                   UPDATE {table} WITH (UPDLOCK, SERIALIZABLE)
                   SET BillableHourlyRate = @Rate, UpdatedAt = @UpdatedAt
                   WHERE OrganizationId = @OrganizationId AND UserId = @UserId;
                   IF @@ROWCOUNT = 0
                       INSERT INTO {table} (OrganizationId, UserId, BillableHourlyRate, UpdatedAt)
                       VALUES (@OrganizationId, @UserId, @Rate, @UpdatedAt);
                   """
                : $"""
                   INSERT INTO {table} (OrganizationId, UserId, BillableHourlyRate, UpdatedAt)
                   VALUES (@OrganizationId, @UserId, @Rate, @UpdatedAt)
                   ON CONFLICT(OrganizationId, UserId) DO UPDATE SET
                       BillableHourlyRate = excluded.BillableHourlyRate,
                       UpdatedAt = excluded.UpdatedAt;
                   """;

            var command = new CommandDefinition(
                sql,
                new
                {
                    OrganizationId = organizationId,
                    UserId = userId,
                    Rate = rate,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                transaction,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private bool IsSqlServer =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.SqlServer",
            StringComparison.Ordinal);

    private string TableName(string name) => IsSqlServer ? $"dbo.{name}" : name;
}
