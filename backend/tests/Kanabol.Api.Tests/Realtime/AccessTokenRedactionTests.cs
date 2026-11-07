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
}
