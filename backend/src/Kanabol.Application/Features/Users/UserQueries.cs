using Kanabol.Domain.Users;

namespace Kanabol.Application.Features.Users;

public static class UserQueries
{
    public static IQueryable<UserDto> ToDtos(this IQueryable<User> users) =>
        users.Select(user => new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.RoleId,
            user.Role.Name,
            user.IsActive,
            user.MustChangePassword,
            user.LockedUntil,
            user.CreatedAt));
}
