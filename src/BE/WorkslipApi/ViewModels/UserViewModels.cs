using Workslip.Application.Users;

namespace Workslip.Api.ViewModels;

public sealed record UserViewModel(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Phone,
    string Role);

public sealed record UserListViewModel(
    IReadOnlyList<UserViewModel> Users,
    int Total);

public static class UserViewModelBuilder
{
    public static UserViewModel ToUser(UserResponse user) => new(
        user.Id,
        user.OrganizationId,
        user.Email,
        user.DisplayName,
        user.Phone,
        user.Role);

    public static UserListViewModel ToUserList(UserListResponse list) => new(
        list.Users.Select(ToUser).ToArray(),
        list.Total);
}
