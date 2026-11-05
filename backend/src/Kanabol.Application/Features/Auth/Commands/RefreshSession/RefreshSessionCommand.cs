using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Auth.Commands.RefreshSession;

public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<AuthSession>;
