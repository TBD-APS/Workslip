using Workslip.Domain;

namespace Workslip.Application.Users;

public interface IUserEntraService
{
    Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct);

    Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct);

    Task<CreateEntraUserResult> InviteAdminAsync(string email, string displayName, CancellationToken ct) =>
        CreateUserAsync(email, displayName, ct);

    Task DeleteUserAsync(string entraUserId, CancellationToken ct);
}
