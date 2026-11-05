using Kanabol.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kanabol.Api.Errors;

/// <summary>
/// What MVC refuses before a handler runs (a missing field, a value of the wrong type) answers like a
/// validator's refusal: code Validation, and the field named as the client sent it (SPEC section 4.6).
/// </summary>
public static class ModelStateProblems
{
    private const string InvalidValue = "The value isn't valid.";

    public static IActionResult Create(ActionContext context)
    {
        var bodies = context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => (Field: FieldName(entry.Key, bodies), Messages: entry.Value!.Errors.Select(Message)))
            .GroupBy(entry => entry.Field)
            .ToDictionary(field => field.Key, field => field.SelectMany(entry => entry.Messages).Distinct().ToArray());

        // MVC adds an entry for the body itself next to the one for the field that broke it.
        if (errors.Count > 1)
        {
            errors.Remove("");
        }

        return new ProblemActionResult(Error.Validation(errors).ToProblemDetails());
    }

    // "$.permissions" from the JSON reader and "Permissions" from model validation are the same field.
    private static string FieldName(string key, HashSet<string> bodies)
    {
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return field == "$" || bodies.Contains(field) ? "" : field;
    }

    private static string Message(ModelError error) =>
        string.IsNullOrEmpty(error.ErrorMessage) ? InvalidValue : error.ErrorMessage;
}
