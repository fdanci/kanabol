using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Kanabol.Api.Authentication;

/// <summary>
/// Until the password is changed, the token only opens the endpoints marked for that, and the
/// public ones. It trusts the token's claim, and changing the password issues a token without it.
/// It runs for every endpoint, so the SignalR hubs are covered without knowing about them.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetails)
    {
        var endpoint = context.GetEndpoint();
        var isBlocked = context.User.HasClaim(AuthClaims.MustChangePassword, "true")
            && endpoint is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint.Metadata.GetMetadata<AllowedBeforePasswordChangeAttribute>() is null;

        if (!isBlocked)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = AuthErrors.PasswordChangeRequired.ToProblemDetails(),
        });
    }
}
