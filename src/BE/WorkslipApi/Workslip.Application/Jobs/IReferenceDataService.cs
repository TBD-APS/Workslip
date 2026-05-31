using Ardalis.Result;

namespace Workslip.Application.Jobs;

public interface IReferenceDataService
{
    Task<Result<ReferenceDataResponse>> GetAsync(CancellationToken cancellationToken);
}
