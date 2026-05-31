namespace Workslip.Application.Jobs;

public interface IReferenceDataRepository
{
    Task<ReferenceDataResponse> GetAsync(Guid organizationId, CancellationToken cancellationToken);
}
