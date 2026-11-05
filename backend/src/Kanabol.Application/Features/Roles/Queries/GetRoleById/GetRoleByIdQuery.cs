using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Roles.Queries.GetRoleById;

public sealed record GetRoleByIdQuery(Guid Id) : IQuery<RoleDto>;
