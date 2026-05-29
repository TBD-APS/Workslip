using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobTaxonomyRepository : IJobTaxonomyRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions TaxonomyCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    public EfJobTaxonomyRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, HybridCache cache)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _cache = cache;
    }

    public async Task<JobTaxonomySnapshot> GetAsync(CancellationToken cancellationToken) =>
        await _cache.GetOrCreateAsync(
            "jobs:taxonomy:v1",
            async token => await _retryPolicy.ExecuteAsync("jobs.taxonomy.get", GetCoreAsync, token),
            TaxonomyCacheOptions,
            tags: ["jobs:taxonomy", "global-configuration"],
            cancellationToken: cancellationToken);

    private async Task<JobTaxonomySnapshot> GetCoreAsync(CancellationToken cancellationToken)
    {
        var workKinds = await _dbContext.JobWorkKinds
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);

        var closureFlags = await _dbContext.JobClosureFlags
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);

        return new(
            workKinds.ToDictionary(row => row.Id, row => new WorkKindDefinition(row.Id, row.Label, row.RequiresCustomWorkKind), StringComparer.OrdinalIgnoreCase),
            closureFlags.ToDictionary(row => row.Id, row => new ClosureFlagDefinition(row.Id, row.Label, row.IsExclusive), StringComparer.OrdinalIgnoreCase));
    }
}
