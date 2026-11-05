using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.UpdateUser;

public sealed record UpdateUserCommand(Guid Id, string Email, string DisplayName) : ICommand<UserDto>;
