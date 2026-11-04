using System.Text.Json;
using Kanabol.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Kanabol.Api.Errors;

public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : Problem(result.Error);

    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        return result.Value is Unit ? new NoContentResult() : new OkObjectResult(result.Value);
    }

    public static IActionResult ToCreatedResult<T>(this Result<T> result, string location) =>
        result.IsSuccess ? new CreatedResult(location, result.Value) : Problem(result.Error);

    private static ProblemActionResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = error is ValidationError validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(
                field => JsonNamingPolicy.CamelCase.ConvertName(field.Key),
                field => field.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = ReasonPhrases.GetReasonPhrase(status);
        problem.Detail = error.Message;
        problem.Extensions["code"] = error.Code;

        return new ProblemActionResult(problem);
    }
}
