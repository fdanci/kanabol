using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Errors;

/// <summary>
/// Writes through ObjectResult, not IProblemDetailsService, which serializes the base type and
/// would drop the fields of a ValidationProblemDetails.
/// </summary>
public sealed class ProblemActionResult(ProblemDetails problem) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context)
    {
        ProblemDetailsEnrichment.AddTraceIds(context.HttpContext, problem);

        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        }.ExecuteResultAsync(context);
    }
}
