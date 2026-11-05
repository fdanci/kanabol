using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Auth.Commands.LogoutUser;

public sealed record LogoutUserCommand(string RefreshToken) : ICommand<Unit>;
