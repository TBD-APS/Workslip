using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Workslip.Application.Common;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfReferenceDataRepository : IReferenceDataRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly HybridCache _cache;
    private readonly ICacheDiagnostics _cacheDiagnostics;

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };

    public EfReferenceDataRepository(
        SqlDbContext dbContext,
        IDatabaseRetryPolicy retryPolicy,
        HybridCache cache,
        ICacheDiagnostics cacheDiagnostics)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _cache = cache;
        _cacheDiagnostics = cacheDiagnostics;
    }

    public async Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        var loaded = false;
        var data = await _cache.GetOrCreateAsync(
            $"reference-data:{organizationId:N}",
            async token =>
            {
                loaded = true;
                _cacheDiagnostics.RecordMiss(CacheRegionNames.ReferenceData);
                var startedAt = Stopwatch.GetTimestamp();

                try
                {
                    var value = await _retryPolicy.ExecuteAsync(
                        "reference-data.get",
                        ct => GetCoreAsync(organizationId, ct),
                        token);
                    _cacheDiagnostics.RecordSet(CacheRegionNames.ReferenceData);
                    return value;
                }
                catch
                {
                    _cacheDiagnostics.RecordFailure(CacheRegionNames.ReferenceData);
                    throw;
                }
                finally
                {
                    _cacheDiagnostics.RecordLoad(
                        CacheRegionNames.ReferenceData,
                        Stopwatch.GetElapsedTime(startedAt));
                }
            },
            CacheOptions,
            tags: ["all", "reference-data", $"org:{organizationId:N}"],
            cancellationToken: cancellationToken);

        if (!loaded)
        {
            _cacheDiagnostics.RecordHit(CacheRegionNames.ReferenceData);
        }

        return data;
    }

    private async Task<ReferenceDataResponse> GetCoreAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.InstallationTypeDefinitions
            .AsNoTracking()
            .AsSplitQuery()
            .Where(d => d.OrganizationId == organizationId)
            .OrderBy(d => d.Name)
            .Select(d => new InstallationTypeDefinitionResponse(
                d.Id,
                d.Name,
                d.SortOrder,
                d.Mappings
                    .OrderBy(m => m.ControlCategory.SortOrder)
                    .ThenBy(m => m.SortOrder)
                    .GroupBy(m => new { m.ControlCategory.Id, m.ControlCategory.Name, m.ControlCategory.SortOrder })
                    .Select(g => new DefinitionCategoryResponse(
                        g.Key.Id,
                        g.Key.Name,
                        g.Key.SortOrder,
                        g.Select(m => new DefinitionControlPointResponse(
                            m.ControlPoint.Id,
                            m.ControlPoint.Name,
                            m.SortOrder,
                            m.IsRequired))
                            .ToArray()))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        var workKinds = await _dbContext.JobWorkKinds
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder)
            .Select(w => new WorkKindResponse(w.Id, w.NormalizedLabel, w.Label, w.RequiresCustomWorkKind, w.SortOrder))
            .ToArrayAsync(cancellationToken);

        var closureFlags = await _dbContext.JobClosureFlags
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => new ClosureFlagResponse(f.Id, f.NormalizedLabel, f.Label, f.IsExclusive, f.SortOrder))
            .ToArrayAsync(cancellationToken);

        return new ReferenceDataResponse(definitions, workKinds, closureFlags);
    }
}
