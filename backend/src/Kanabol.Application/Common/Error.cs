namespace Kanabol.Application.Common;

public record Error(string Code, string Message, ErrorType Type)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static ValidationError Validation(IReadOnlyDictionary<string, string[]> errors) => new(errors);
}
