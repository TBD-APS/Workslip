using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public interface IJobTaxonomyRepository
{
    Task<JobTaxonomySnapshot> GetAsync(CancellationToken cancellationToken);
}