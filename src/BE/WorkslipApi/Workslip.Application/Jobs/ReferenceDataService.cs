using Ardalis.Result;
using Workslip.Application.Auth;

namespace Workslip.Application.Jobs;

public sealed class ReferenceDataService : IReferenceDataService
{
    private readonly IReferenceDataRepository _repository;
    private readonly ICurrentUserContext _currentUser;

    public ReferenceDataService(IReferenceDataRepository repository, ICurrentUserContext currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<ReferenceDataResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null)
            return Result<ReferenceDataResponse>.Forbidden();

        var data = await _repository.GetAsync(orgId.Value, cancellationToken);
        return Result<ReferenceDataResponse>.Success(data);
    }
}
