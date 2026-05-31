using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfReferenceDataRepository : IReferenceDataRepository
{
    private readonly SqlDbContext _dbContext;

    public EfReferenceDataRepository(SqlDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReferenceDataResponse> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.InstallationTypeDefinitions
            .AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .OrderBy(d => d.SortOrder)
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
                            m.ControlPoint.Description,
                            m.SortOrder,
                            m.IsRequired))
                            .ToArray()))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        var workKinds = await _dbContext.JobWorkKinds
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder)
            .Select(w => new WorkKindResponse(w.Id, w.Label, w.RequiresCustomWorkKind, w.SortOrder))
            .ToArrayAsync(cancellationToken);

        var closureFlags = await _dbContext.JobClosureFlags
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => new ClosureFlagResponse(f.Id, f.Label, f.IsExclusive, f.SortOrder))
            .ToArrayAsync(cancellationToken);

        return new ReferenceDataResponse(definitions, workKinds, closureFlags);
    }
}
