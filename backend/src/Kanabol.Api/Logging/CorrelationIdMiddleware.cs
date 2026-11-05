using System.Text.RegularExpressions;
using Serilog.Context;

namespace Kanabol.Api.Logging;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = FromRequest(context) ?? Guid.NewGuid().ToString();
        context.Items[ItemKey] = correlationId;

        // Set as the response starts, because an exception handler clears the headers set before that.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    // The value comes from the client and ends up in logs and headers, so only a plain id is accepted.
    private static string? FromRequest(HttpContext context)
    {
        var value = context.Request.Headers[HeaderName].ToString();
        return SafeId().IsMatch(value) ? value : null;
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex SafeId();
}
