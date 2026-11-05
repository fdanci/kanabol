using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserDto>;
