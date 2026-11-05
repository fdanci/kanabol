using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Roles.Commands.DeleteRole;

public sealed record DeleteRoleCommand(Guid Id) : ICommand<Unit>;
