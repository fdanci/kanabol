using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Auth.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<AuthSession>;
