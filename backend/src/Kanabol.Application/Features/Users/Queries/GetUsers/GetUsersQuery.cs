using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Users.Queries.GetUsers;

public sealed record GetUsersQuery(int Page, int PageSize) : IQuery<Page<UserDto>>;
