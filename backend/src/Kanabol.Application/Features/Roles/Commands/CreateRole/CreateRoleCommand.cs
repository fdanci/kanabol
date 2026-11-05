using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Roles.Commands.CreateRole;

public sealed record CreateRoleCommand(string Name, IReadOnlyList<string> Permissions) : ICommand<RoleDto>;
