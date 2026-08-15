namespace Workslip.Application.Users;

public interface ISuperadminEntraService : IUserEntraService
{
    Task<CreateEntraUserResult> EnsureSuperadminAsync(
        string email,
        string displayName,
        CancellationToken cancellationToken);

    Task RevokeSuperadminAsync(
        string entraUserId,
        CancellationToken cancellationToken);
}
