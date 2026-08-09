using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Api.ViewModels;

public sealed record AdminUserViewModel(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Email,
    string DisplayName,
    string Phone,
    string Role,
    string RoleDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminUserListViewModel(
    IReadOnlyList<AdminUserViewModel> Users,
    int Total);

public static class AdminUserViewModelBuilder
{
    public static AdminUserViewModel ToAdminUser(AdminUserResponse user) => new(
        user.Id,
        user.OrganizationId,
        user.OrganizationName,
        user.Email,
        user.DisplayName,
        user.Phone,
        user.Role,
        GetRoleDisplayName(user.Role),
        user.CreatedAt,
        user.UpdatedAt);

    public static AdminUserListViewModel ToAdminUserList(AdminUserListResponse list) => new(
        list.Users.Select(ToAdminUser).ToArray(),
        list.Total);

    private static string GetRoleDisplayName(string role) => role switch
    {
        Roles.Superadmin => "Superadministrator",
        Roles.Admin => "Administrator",
        Roles.User => "Medarbejder",
        Roles.Auditor => "Auditør",
        _ => role
    };
}
