using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.ChangeUserRole;

public sealed record ChangeUserRoleCommand(Guid Id, Guid RoleId) : ICommand<UserDto>;
