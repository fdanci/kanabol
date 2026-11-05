using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.ActivateUser;

public sealed record ActivateUserCommand(Guid Id) : ICommand<UserDto>;
