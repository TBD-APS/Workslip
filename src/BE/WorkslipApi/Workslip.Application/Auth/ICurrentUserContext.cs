namespace Workslip.Application.Auth;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string? Role { get; }
}
