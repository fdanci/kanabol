namespace Kanabol.Application.Features.Roles;

public sealed record RoleDto(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);
