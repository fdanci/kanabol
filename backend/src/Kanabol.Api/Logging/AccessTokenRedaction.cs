using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Kanabol.Api.Logging;

/// <summary>
/// The hubs take the access token from the query string (SPEC section 4.5), and ASP.NET's own request log
/// writes the whole address, query included, which Development shows (SPEC section 8.1). Tokens never go in
/// a log, so every property loses the value of access_token before any sink sees it: text, a Uri, which
/// then goes as text, and every text inside a list, an object or a dictionary, keys included, at any depth.
/// A value that holds no token stays the same instance, so most entries cost a look and no copy.
/// </summary>
public sealed partial class AccessTokenRedaction : ILogEventEnricher
{
    private const string Name = "access_token=";
    private const string Hidden = "access_token=[hidden]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // A copy, since replacing a property changes the dictionary.
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        ScalarValue scalar => Redact(scalar),
        SequenceValue sequence => Redact(sequence),
        StructureValue structure => Redact(structure),
        DictionaryValue dictionary => Redact(dictionary),
        _ => value,
    };

    private static ScalarValue Redact(ScalarValue scalar) => scalar.Value switch
    {
        string text when Holds(text) => new ScalarValue(AccessToken().Replace(text, Hidden)),
        Uri uri when Holds(uri.OriginalString) => new ScalarValue(AccessToken().Replace(uri.OriginalString, Hidden)),
        _ => scalar,
    };

    private static SequenceValue Redact(SequenceValue sequence)
    {
        var elements = sequence.Elements.Select(Redact).ToList();
        return Changed(elements, sequence.Elements) ? new SequenceValue(elements) : sequence;
    }

    private static StructureValue Redact(StructureValue structure)
    {
        var values = structure.Properties.Select(property => Redact(property.Value)).ToList();
        return Changed(values, structure.Properties.Select(property => property.Value).ToList())
            ? new StructureValue(structure.Properties.Select((property, i) => new LogEventProperty(property.Name, values[i])), structure.TypeTag)
            : structure;
    }

    private static DictionaryValue Redact(DictionaryValue dictionary)
    {
        var elements = dictionary.Elements
            .Select(element => new KeyValuePair<ScalarValue, LogEventPropertyValue>(Redact(element.Key), Redact(element.Value)))
            .ToList();
        var changed = elements.Zip(dictionary.Elements)
            .Any(pair => !ReferenceEquals(pair.First.Key, pair.Second.Key) || !ReferenceEquals(pair.First.Value, pair.Second.Value));
        return changed ? new DictionaryValue(elements) : dictionary;
    }

    private static bool Changed(IReadOnlyList<LogEventPropertyValue> redacted, IReadOnlyList<LogEventPropertyValue> original) =>
        redacted.Where((value, i) => !ReferenceEquals(value, original[i])).Any();

    private static bool Holds(string text) => text.Contains(Name, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("access_token=[^&\\s\"]*", RegexOptions.IgnoreCase)]
    private static partial Regex AccessToken();
}
