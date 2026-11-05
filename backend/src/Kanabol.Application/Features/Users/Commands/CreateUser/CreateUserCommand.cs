using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.CreateUser;

public sealed record CreateUserCommand(string Email, string DisplayName, Guid RoleId, string InitialPassword) : ICommand<UserDto>;
