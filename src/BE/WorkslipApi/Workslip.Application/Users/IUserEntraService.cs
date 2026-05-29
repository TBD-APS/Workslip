namespace Workslip.Application.Users;


public interface IUserEntraService
{
    Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct);
}