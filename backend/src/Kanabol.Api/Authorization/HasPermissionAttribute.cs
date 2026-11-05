using Microsoft.AspNetCore.Authorization;

namespace Kanabol.Api.Authorization;

/// <summary>
/// Closes an endpoint to anyone whose token doesn't have the permission (SPEC section 4.4). Several
/// of them, on an action or on its controller, all have to hold.
/// </summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicy.NameFor(permission))
{
    public string Permission { get; } = permission;
}
