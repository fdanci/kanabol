using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Roles.Queries.GetRoles;

public sealed record GetRolesQuery(int PageNumber, int PageSize) : IQuery<Page<RoleDto>>;
