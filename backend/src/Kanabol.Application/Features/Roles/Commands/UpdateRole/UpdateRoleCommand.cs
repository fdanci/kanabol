using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Roles.Commands.UpdateRole;

public sealed record UpdateRoleCommand(Guid Id, string Name, IReadOnlyList<string> Permissions) : ICommand<RoleDto>;
