namespace Kanabol.Application.Common;

public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("Validation", "One or more validation errors occurred.", ErrorType.Validation);
