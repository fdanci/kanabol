using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Kanabol.Api.Authorization;

/// <summary>
/// Builds the policy for a permission when it's asked for, so they don't have to be registered one by
/// one (SPEC section 4.4). A name that isn't in Permissions.All gets a policy too, which nobody can
/// satisfy: a misspelt permission closes its endpoint.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
            .Build();
    }
}
