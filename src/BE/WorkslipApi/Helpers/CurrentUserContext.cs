using System.Security.Claims;
using Workslip.Application.Auth;

namespace Workslip.Api.Helpers;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";

    public Guid? UserId => TryGetGuid(WorkslipUserIdClaim) ?? TryGetGuid(ClaimTypes.NameIdentifier) ?? TryGetGuid("sub");

    public Guid? OrganizationId => TryGetGuid(OrganizationIdClaim);

    public string? Role => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role)
        ?? httpContextAccessor.HttpContext?.User.FindFirstValue("roles");

    private Guid? TryGetGuid(string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            return null;
        }

        var value = httpContextAccessor.HttpContext?.User.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
