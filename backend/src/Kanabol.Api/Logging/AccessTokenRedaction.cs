using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Kanabol.Api.Logging;

/// <summary>
/// The hubs take the access token from the query string (SPEC section 4.5), and ASP.NET's own request log
/// writes the whole address, query included, which Development shows (SPEC section 8.1). Tokens never go in
/// a log, so every text property loses the value of access_token before any sink sees it.
/// </summary>
public sealed partial class AccessTokenRedaction : ILogEventEnricher
{
    private const string Hidden = "access_token=[hidden]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // A copy, since replacing a property changes the dictionary.
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue { Value: string text } && text.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(AccessToken().Replace(text, Hidden))));
            }
        }
    }

    [GeneratedRegex("access_token=[^&\\s\"]*", RegexOptions.IgnoreCase)]
    private static partial Regex AccessToken();
}
