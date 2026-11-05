using Kanabol.Application.Abstractions;
using Serilog;
using Serilog.Context;

namespace Kanabol.Api.Logging;

/// <summary>
/// Adds the signed-in user's id to the logs. It has to run after authentication, which is what
/// sets the user on the request.
/// </summary>
public sealed class UserIdLogContextMiddleware(RequestDelegate next)
{
    public const string PropertyName = "UserId";

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IDiagnosticContext diagnostics)
    {
        if (!currentUser.IsAuthenticated)
        {
            await next(context);
            return;
        }

        // The request's summary line is written outside this scope, so it needs the id separately.
        diagnostics.Set(PropertyName, currentUser.Id);

        using (LogContext.PushProperty(PropertyName, currentUser.Id))
        {
            await next(context);
        }
    }
}
