namespace Workslip.Application.Auth;

public interface IUserClaimsCacheInvalidator
{
    void Invalidate(string? entraId, string? email, string? entraEmail);
}
