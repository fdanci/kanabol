using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Commands.ResetUserPassword;

public sealed record ResetUserPasswordCommand(Guid Id, string InitialPassword) : ICommand<UserDto>;
