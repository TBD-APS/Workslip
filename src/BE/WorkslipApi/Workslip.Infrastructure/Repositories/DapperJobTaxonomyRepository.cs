using Dapper;
using Microsoft.Extensions.Caching.Hybrid;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperJobTaxonomyRepository(
    ISqlConnectionFactory connectionFactory,
    IDatabaseRetryPolicy retryPolicy,
    HybridCache cache) : IJobTaxonomyRepository
{
    private static readonly HybridCacheEntryOptions TaxonomyCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task<JobTaxonomySnapshot> GetAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            "jobs:taxonomy:v1",
            async token => await retryPolicy.ExecuteAsync("jobs.taxonomy.get", GetCoreAsync, token),
            TaxonomyCacheOptions,
            tags: ["jobs:taxonomy", "global-configuration"],
            cancellationToken: cancellationToken);

    private async Task<JobTaxonomySnapshot> GetCoreAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var workKinds = await connection.QueryAsync<JobWorkKindRow>(new CommandDefinition(
            """
            select Id, Label, RequiresCustomWorkKind, IsActive, SortOrder, UpdatedAt
            from dbo.JobWorkKinds
            where IsActive = 1
            order by SortOrder, Id;
            """,
            cancellationToken: cancellationToken));

        var closureFlags = await connection.QueryAsync<JobClosureFlagRow>(new CommandDefinition(
            """
            select Id, Label, IsExclusive, IsActive, SortOrder, UpdatedAt
            from dbo.JobClosureFlags
            where IsActive = 1
            order by SortOrder, Id;
            """,
            cancellationToken: cancellationToken));

        return new(
            workKinds.ToDictionary(row => row.Id, row => new WorkKindDefinition(row.Id, row.Label, row.RequiresCustomWorkKind), StringComparer.OrdinalIgnoreCase),
            closureFlags.ToDictionary(row => row.Id, row => new ClosureFlagDefinition(row.Id, row.Label, row.IsExclusive), StringComparer.OrdinalIgnoreCase));
    }
}
