using System.Globalization;
using Kanabol.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The API's own log, as Serilog writes it with the host's enrichers (Tasks.md group 19; SPEC section 8.1):
/// a token in a destructured object, a list, a dictionary or a Uri never reaches a sink.
/// </summary>
public sealed class ApiLogRedactionTests(ApiFixture api) : ApiTest(api)
{
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJsb2cifQ.c2VjcmV0LWluLWEtbG9n";

    [Fact]
    public void Log_OfStructuredValuesHoldingAToken_ReachesTheSinkWithoutIt()
    {
        var marker = Guid.NewGuid().ToString("N");
        var address = $"/hubs/board?id=x&access_token={Token}";
        var logger = Api.Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Kanabol.Api.Tests.Redaction");

        logger.LogInformation(
            "Probe {Marker} {@Request} {Addresses} {@Headers} {Uri}",
            marker,
            new { Method = "GET", Address = address, Retries = new[] { address } },
            new List<string> { "/api/projects", address },
            new Dictionary<string, string> { ["Referer"] = address, ["Accept"] = "*/*" },
            new Uri("http://api:8080" + address));

        var logged = Api.Factory.Logs.Events.Should().ContainSingle(entry =>
            entry.Properties.ContainsKey("Marker") && entry.Properties["Marker"].ToString().Contains(marker, StringComparison.Ordinal)).Subject;
        foreach (var (name, value) in logged.Properties)
        {
            value.ToString(format: null, CultureInfo.InvariantCulture).Should().NotContain(Token, $"{name} reaches the sinks");
        }

        logged.RenderMessage(CultureInfo.InvariantCulture).Should().NotContain(Token).And.Contain("access_token=[hidden]");
    }
}
