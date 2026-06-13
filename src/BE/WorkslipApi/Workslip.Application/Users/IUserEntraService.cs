namespace Workslip.Application.Users;


public interface IUserEntraService
{
    Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct);

    Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct);

    Task DeleteUserAsync(string entraUserId, CancellationToken ct);
}
