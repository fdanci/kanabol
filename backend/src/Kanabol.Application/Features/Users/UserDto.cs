namespace Kanabol.Application.Features.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid RoleId,
    string RoleName,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt);
