namespace Kanabol.Application.Features.Auth;

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string RoleName,
    bool MustChangePassword,
    IReadOnlyList<string> Permissions);
