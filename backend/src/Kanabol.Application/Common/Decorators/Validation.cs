using FluentValidation;

namespace Kanabol.Application.Common.Decorators;

internal static class Validation
{
    public static async Task<ValidationError?> Run<T>(T request, IEnumerable<IValidator<T>> validators, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(request, ct);
            failures.AddRange(outcome.Errors);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        return Error.Validation(failures
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(field => field.Key, field => field.Select(failure => failure.ErrorMessage).ToArray()));
    }
}
