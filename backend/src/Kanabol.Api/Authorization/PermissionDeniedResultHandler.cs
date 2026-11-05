using Kanabol.Api.Errors;
using Kanabol.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Kanabol.Api.Authorization;

/// <summary>
/// A refusal gets a code the frontend can show, and the log says what was missing, which the answer
/// doesn't. Everything else goes on as usual, including the 401 for a request without a valid token.
/// </summary>
public sealed class PermissionDeniedResultHandler(
    IProblemDetailsService problemDetails,
    ILogger<PermissionDeniedResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var missing = authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<PermissionRequirement>()
            .Select(requirement => requirement.Permission)
            .ToArray() ?? [];

        logger.LogWarning(
            "Refused {Method} {Path}, which needs {MissingPermissions}",
            context.Request.Method,
            context.Request.Path,
            missing);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = AuthErrors.PermissionRequired.ToProblemDetails(),
        });
    }
}
