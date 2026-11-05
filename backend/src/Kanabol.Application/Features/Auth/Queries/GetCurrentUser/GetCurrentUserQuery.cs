using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Auth.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserDto>;
