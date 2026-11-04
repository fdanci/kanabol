using System.Diagnostics;
using Kanabol.Api.Logging;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Errors;

public static class ProblemDetailsEnrichment
{
    public static void AddTraceIds(ProblemDetailsContext context) =>
        AddTraceIds(context.HttpContext, context.ProblemDetails);

    /// <summary>Every error answer says how to find it in the logs.</summary>
    public static void AddTraceIds(HttpContext httpContext, ProblemDetails problem)
    {
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
        {
            problem.Extensions["correlationId"] = correlationId;
        }
    }
}
