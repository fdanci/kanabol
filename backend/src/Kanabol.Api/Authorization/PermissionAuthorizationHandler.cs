using Kanabol.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Kanabol.Api.Authorization;

/// <summary>Reads the token's claims, so a change to someone's role reaches this check with their next token.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(AuthClaims.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
