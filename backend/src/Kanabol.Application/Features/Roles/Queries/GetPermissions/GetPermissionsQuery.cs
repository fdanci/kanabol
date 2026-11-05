using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Roles.Queries.GetPermissions;

public sealed record GetPermissionsQuery : IQuery<string[]>;
