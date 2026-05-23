namespace Workslip.Application.Users;


public interface IUserEntraService
{
    Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, string appRoleValue, CancellationToken ct);
}