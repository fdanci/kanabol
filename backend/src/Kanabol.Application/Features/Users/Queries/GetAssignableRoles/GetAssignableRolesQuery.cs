using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Users.Queries.GetAssignableRoles;

public sealed record GetAssignableRolesQuery : IQuery<AssignableRoleDto[]>;
