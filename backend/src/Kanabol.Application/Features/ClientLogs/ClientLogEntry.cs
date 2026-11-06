namespace Kanabol.Application.Features.ClientLogs;

/// <summary>
/// One warning or error from the browser's Logger (SPEC section 8.3). The correlation id is the one of
/// the API request it's about, if any, so its line in Seq leads to that request's lines.
/// </summary>
public sealed record ClientLogEntry(string Level, string Message, string? Url, string? CorrelationId, string? Details);
