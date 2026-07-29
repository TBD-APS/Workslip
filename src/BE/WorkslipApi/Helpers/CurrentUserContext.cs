using System.Security.Claims;
using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Api.Helpers;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public const string OrganizationScopeHeader = "X-Workslip-Organization-Id";

    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";

    public Guid? UserId =>
        TryGetClaimGuid(WorkslipUserIdClaim)
        ?? TryGetClaimGuid(ClaimTypes.NameIdentifier)
        ?? TryGetClaimGuid("sub");

    public Guid? OrganizationId =>
        string.Equals(Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
            ? TryGetHeaderGuid(OrganizationScopeHeader)
            : TryGetClaimGuid(OrganizationIdClaim);

    public string? Role =>
        User?.FindFirstValue(ClaimTypes.Role)
        ?? User?.FindFirstValue("roles")
        ?? User?.FindFirstValue("role");

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    private Guid? TryGetClaimGuid(string claimType)
    {
        var value = User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private Guid? TryGetHeaderGuid(string headerName)
    {
        var value = httpContextAccessor.HttpContext?.Request.Headers[headerName].FirstOrDefault();
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
