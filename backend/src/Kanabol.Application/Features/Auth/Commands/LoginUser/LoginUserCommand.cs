using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Auth.Commands.LoginUser;

public sealed record LoginUserCommand(string Email, string Password) : ICommand<AuthSession>;
