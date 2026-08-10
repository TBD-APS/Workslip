using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public static class UserResponseBuilder
{
    public static UserResponse MapToResponse(UserDataRow user) =>
        new(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.Role,
            user.UserKind);
}
