using System.Security.Claims;
using Workslip.Application.Auth;

namespace Workslip.Api.Helpers;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId =>
        TryGetGuid(WorkslipUserIdClaim)
        ?? TryGetGuid(ClaimTypes.NameIdentifier)
        ?? TryGetGuid("sub");

    public Guid? OrganizationId =>
        TryGetGuid(OrganizationIdClaim);

    public string? Role =>
        User?.FindFirstValue(ClaimTypes.Role)
        ?? User?.FindFirstValue("roles")
        ?? User?.FindFirstValue("role");

    private ClaimsPrincipal? User =>
        _httpContextAccessor.HttpContext?.User;

    private Guid? TryGetGuid(string claimType)
    {
        var value = User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
