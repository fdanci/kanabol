using Microsoft.AspNetCore.Authorization;

namespace Kanabol.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
