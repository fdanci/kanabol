using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.DeactivateUser;

public sealed record DeactivateUserCommand(Guid Id) : ICommand<UserDto>;
