using System.Security.Claims;
using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Api.Helpers;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public const string OrganizationScopeHeader = "X-Workslip-Organization-Id";
    public const string ValidatedOrganizationScopeItem = "Workslip.ValidatedOrganizationScope";

    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";

    public Guid? UserId =>
        TryGetClaimGuid(WorkslipUserIdClaim)
        ?? TryGetClaimGuid(ClaimTypes.NameIdentifier)
        ?? TryGetClaimGuid("sub");

    public Guid? OrganizationId =>
        string.Equals(Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
            ? GetValidatedOrganizationScope()
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

    private Guid? GetValidatedOrganizationScope() =>
        httpContextAccessor.HttpContext?.Items.TryGetValue(
            ValidatedOrganizationScopeItem,
            out var value) == true && value is Guid organizationId
                ? organizationId
                : null;
}
