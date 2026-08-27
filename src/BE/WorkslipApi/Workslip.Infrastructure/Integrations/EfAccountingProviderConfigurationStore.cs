using Dapper;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Integrations;

public sealed class EfAccountingProviderConfigurationStore(
    ISqlConnectionFactory connectionFactory,
    IDatabaseRetryPolicy retryPolicy) : IAccountingProviderConfigurationStore
{
    public Task<string?> GetProviderAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "accounting-provider-settings.get",
            token => GetProviderCoreAsync(organizationId, token),
            cancellationToken);

    public Task<bool> SetProviderAsync(
        Guid organizationId,
        string? providerId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "accounting-provider-settings.update",
            token => SetProviderCoreAsync(organizationId, providerId, token),
            cancellationToken);

    private async Task<string?> GetProviderCoreAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ProviderId
            FROM dbo.OrganizationAccountingSettings
            WHERE OrganizationId = @OrganizationId;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));
    }

    private async Task<bool> SetProviderCoreAsync(
        Guid organizationId,
        string? providerId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM dbo.Organizations
                WHERE Id = @OrganizationId
            )
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            IF @ProviderId IS NULL
            BEGIN
                DELETE FROM dbo.OrganizationAccountingSettings
                WHERE OrganizationId = @OrganizationId;
            END
            ELSE
            BEGIN
                MERGE dbo.OrganizationAccountingSettings AS target
                USING (SELECT @OrganizationId AS OrganizationId) AS source
                    ON target.OrganizationId = source.OrganizationId
                WHEN MATCHED THEN
                    UPDATE SET ProviderId = @ProviderId, UpdatedAt = @UpdatedAt
                WHEN NOT MATCHED THEN
                    INSERT (OrganizationId, ProviderId, UpdatedAt)
                    VALUES (@OrganizationId, @ProviderId, @UpdatedAt);
            END;

            SELECT CAST(1 AS bit);
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            sql,
            new
            {
                OrganizationId = organizationId,
                ProviderId = providerId,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken));
    }
}
