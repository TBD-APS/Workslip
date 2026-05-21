namespace Workslip.Application.Organizations;

public interface IOrganizationRepository
{
    Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken);
    Task<OrganizationOnboardingResponse?> CreateAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken);
    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
