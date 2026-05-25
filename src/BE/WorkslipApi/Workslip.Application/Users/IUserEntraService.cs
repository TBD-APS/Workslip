namespace Workslip.Application.Users;


public interface IUserEntraService
{
    Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct);
    Task AssignAppRoleTo(string entraUserId, string appRoleValue, CancellationToken ct);
}