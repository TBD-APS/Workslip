using Workslip.Application.Organizations;

namespace Workslip.Api.ViewModels;

public sealed record OrganizationViewModel(
    Guid Id,
    string Name,
    string Cvr);

public sealed record OrganizationUserViewModel(
    Guid Id,
    Guid OrganizationId,
    string DisplayName,
    string? Email,
    string? Phone,
    string Role);

public sealed record OrganizationOnboardingViewModel(
    OrganizationViewModel Organization,
    OrganizationUserViewModel User);

public sealed record CurrentUserViewModel(
    Guid Id,
    string DisplayName,
    string? Email,
    string? Phone,
    string Role,
    OrganizationViewModel Organization);

public static class OrganizationViewModelBuilder
{
    public static OrganizationViewModel ToOrganization(OrganizationResponse organization) => new(
        organization.Id,
        organization.Name,
        organization.Cvr);

    public static OrganizationUserViewModel ToOrganizationUser(OrganizationUserResponse user) => new(
        user.Id,
        user.OrganizationId,
        user.DisplayName,
        user.Email,
        user.Phone,
        user.Role);

    public static OrganizationOnboardingViewModel ToOnboarding(OrganizationOnboardingResponse onboarding) => new(
        ToOrganization(onboarding.Organization),
        ToOrganizationUser(onboarding.User));

    public static CurrentUserViewModel ToCurrentUser(CurrentUserResponse user) => new(
        user.Id,
        user.DisplayName,
        user.Email,
        user.Phone,
        user.Role,
        ToOrganization(user.Organization));
}
