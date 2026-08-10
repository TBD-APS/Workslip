using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Api.ViewModels;

public sealed record UserViewModel(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Phone,
    string Role,
    string RoleDisplayName,
    string UserKind,
    decimal? HoursThisWeek,
    decimal? HoursThisMonth,
    decimal? HoursBiweekly);

public sealed record UserListViewModel(
    IReadOnlyList<UserViewModel> Users,
    int Total);

public sealed record AssignedJobViewModel(
    Guid ReportId,
    string? ReportNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerAddress);

public sealed record UserDetailViewModel(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Phone,
    string Role,
    string RoleDisplayName,
    string UserKind,
    IReadOnlyList<AssignedJobViewModel> AssignedJobs,
    decimal? TotalHours,
    decimal? HoursThisWeek,
    decimal? HoursThisMonth,
    decimal? HoursBiweekly);

public static class UserViewModelBuilder
{
    public static UserViewModel ToUser(UserResponse user) => new(
        user.Id,
        user.OrganizationId,
        user.Email,
        user.DisplayName,
        user.Phone,
        user.Role,
        GetRoleDisplayName(user.Role),
        user.UserKind,
        user.HoursThisWeek,
        user.HoursThisMonth,
        user.HoursBiweekly);

    public static UserListViewModel ToUserList(UserListResponse list) => new(
        list.Users.Select(ToUser).ToArray(),
        list.Total);

    public static UserDetailViewModel ToUserDetail(UserDetailResponse detail) => new(
        detail.Id,
        detail.OrganizationId,
        detail.Email,
        detail.DisplayName,
        detail.Phone,
        detail.Role,
        GetRoleDisplayName(detail.Role),
        detail.UserKind,
        detail.AssignedJobs.Select(j => new AssignedJobViewModel(
            j.ReportId,
            j.ReportNumber,
            j.Status,
            j.UpdatedAt,
            j.CustomerName,
            j.CustomerEmail,
            j.CustomerAddress)).ToArray(),
        detail.TotalHours,
        detail.HoursThisWeek,
        detail.HoursThisMonth,
        detail.HoursBiweekly);

    private static string GetRoleDisplayName(string role) => role switch
    {
        Roles.Superadmin => "Superadministrator",
        Roles.Admin => "Administrator",
        Roles.User => "Medarbejder",
        Roles.Auditor => "Auditør",
        _ => role
    };
}
