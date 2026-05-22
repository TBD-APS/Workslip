using Dapper;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperJobTaxonomyRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy) : IJobTaxonomyRepository
{
    public Task<JobTaxonomySnapshot> GetAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.taxonomy.get", GetCoreAsync, cancellationToken);

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
