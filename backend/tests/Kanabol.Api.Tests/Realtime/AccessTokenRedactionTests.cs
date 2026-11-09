using System.Globalization;
using Kanabol.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The hubs take the access token from the query string (SPEC section 4.5), and ASP.NET's own request log
/// writes the whole address, which Development shows (SPEC section 8.1). No log keeps a token.
/// </summary>
public sealed class AccessTokenRedactionTests
{
    private static LogEvent Logged(string queryString) =>
        new(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("Request starting {Method} {Path}{QueryString}"),
            [
                new LogEventProperty("Method", new ScalarValue("GET")),
                new LogEventProperty("Path", new ScalarValue("/hubs/board")),
                new LogEventProperty("QueryString", new ScalarValue(queryString)),
            ]);

    private static string Rendered(LogEvent logEvent)
    {
        new AccessTokenRedaction().Enrich(logEvent, propertyFactory: null!);
        return logEvent.RenderMessage(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Enrich_OfAnAddressWithAnAccessToken_HidesTheTokenAndKeepsTheRest()
    {
        Rendered(Logged("?id=abc&access_token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl&negotiateVersion=1"))
            .Should().Be("Request starting \"GET\" \"/hubs/board\"\"?id=abc&access_token=[hidden]&negotiateVersion=1\"");
    }

    [Fact]
    public void Enrich_OfATokenAtTheEnd_HidesIt()
    {
        Rendered(Logged("?id=abc&access_token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl"))
            .Should().NotContain("c2lnbmF0dXJl").And.EndWith("access_token=[hidden]\"");
    }

    [Fact]
    public void Enrich_OfAnAddressWithoutAToken_ChangesNothing()
    {
        Rendered(Logged("?page=1&pageSize=50")).Should().Be("Request starting \"GET\" \"/hubs/board\"\"?page=1&pageSize=50\"");
    }

    // ---- Structured values (Tasks.md group 19; SPEC section 8.1): lists, objects, dictionaries and a Uri, at any depth.
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl";
    private const string WithToken = "/hubs/board?id=abc&access_token=" + Token;
    private const string Hidden = "/hubs/board?id=abc&access_token=[hidden]";

    private static LogEventPropertyValue Redacted(LogEventPropertyValue value)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("{Value}"),
            [new LogEventProperty("Value", value)]);
        new AccessTokenRedaction().Enrich(logEvent, propertyFactory: null!);
        return logEvent.Properties["Value"];
    }

    private static string Text(LogEventPropertyValue value) => value.ToString(format: null, CultureInfo.InvariantCulture);

    [Fact]
    public void Enrich_OfAListHoldingAToken_HidesItAndKeepsTheOtherItems()
    {
        var value = Redacted(new SequenceValue([new ScalarValue("first"), new ScalarValue(WithToken), new ScalarValue(3)]));

        value.Should().BeOfType<SequenceValue>().Which.Elements.Select(Text)
            .Should().Equal("\"first\"", $"\"{Hidden}\"", "3");
    }

    [Fact]
    public void Enrich_OfAnObjectHoldingAToken_HidesIt_AndKeepsItsTypeAndOtherProperties()
    {
        var value = Redacted(new StructureValue(
            [new LogEventProperty("Method", new ScalarValue("GET")), new LogEventProperty("Address", new ScalarValue(WithToken))],
            typeTag: "Request"));

        var structure = value.Should().BeOfType<StructureValue>().Subject;
        structure.TypeTag.Should().Be("Request");
        structure.Properties.Select(property => $"{property.Name}={Text(property.Value)}")
            .Should().Equal("Method=\"GET\"", $"Address=\"{Hidden}\"");
    }

    [Fact]
    public void Enrich_OfADictionaryHoldingAToken_HidesItInItsValuesAndKeys()
    {
        var value = Redacted(new DictionaryValue(
        [
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("Referer"), new ScalarValue(WithToken)),
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(WithToken), new ScalarValue("as a key")),
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("Accept"), new ScalarValue("*/*")),
        ]));

        Text(value).Should().NotContain(Token)
            .And.Contain($"(\"Referer\": \"{Hidden}\")")
            .And.Contain($"(\"{Hidden}\": \"as a key\")")
            .And.Contain("(\"Accept\": \"*/*\")");
    }

    [Fact]
    public void Enrich_OfAUriHoldingAToken_WritesItAsTextWithoutTheToken()
    {
        var value = Redacted(new ScalarValue(new Uri("https://kanabol.example" + WithToken)));

        value.Should().BeOfType<ScalarValue>().Which.Value.Should().Be("https://kanabol.example" + Hidden);
    }

    [Fact]
    public void Enrich_OfAUriWithoutAToken_KeepsTheUri()
    {
        var address = new Uri("https://kanabol.example/api/projects?page=1");

        Redacted(new ScalarValue(address)).Should().BeOfType<ScalarValue>().Which.Value.Should().BeSameAs(address);
    }

    [Fact]
    public void Enrich_OfATokenDeepInside_HidesItAtAnyDepth()
    {
        var value = Redacted(new DictionaryValue(
        [
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue("Requests"),
                new SequenceValue(
                [
                    new StructureValue([new LogEventProperty("Address", new ScalarValue(new Uri("http://api" + WithToken)))]),
                    new StructureValue([new LogEventProperty("Tags", new SequenceValue([new ScalarValue(WithToken)]))]),
                ])),
        ]));

        Text(value).Should().NotContain(Token).And.Contain($"http://api{Hidden}").And.Contain($"\"{Hidden}\"");
    }

    // Most entries hold no token: nothing in them is copied.
    [Fact]
    public void Enrich_OfStructuredValuesWithoutAToken_KeepsTheSameValues()
    {
        SequenceValue list = new([new ScalarValue("a"), new StructureValue([new LogEventProperty("B", new ScalarValue("b"))])]);
        DictionaryValue dictionary = new([new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("k"), list)]);

        Redacted(dictionary).Should().BeSameAs(dictionary);
    }
}
