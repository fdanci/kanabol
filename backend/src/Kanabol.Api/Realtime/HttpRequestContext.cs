using Kanabol.Application.Abstractions;

namespace Kanabol.Api.Realtime;

/// <summary>
/// Reads X-Connection-Id from the request. The id goes out to everyone on the project with the events the
/// request causes, so only one that looks like SignalR's own passes: letters, digits, '-' and '_', at most
/// 64 of them, sent once. SignalR's connection id is public; the token that opens the connection isn't it.
/// </summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public const string ConnectionIdHeader = "X-Connection-Id";

    private const int MaxLength = 64;

    public string? ConnectionId
    {
        get
        {
            var values = accessor.HttpContext?.Request.Headers[ConnectionIdHeader] ?? default;
            return values.Count == 1 && IsAnId(values[0]) ? values[0] : null;
        }
    }

    private static bool IsAnId(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
