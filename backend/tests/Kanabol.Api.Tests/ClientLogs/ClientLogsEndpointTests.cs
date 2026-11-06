using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Kanabol.Api.Tests.Support;
using Serilog.Events;

namespace Kanabol.Api.Tests.ClientLogs;

/// <summary>SPEC section 8.3: the browser's warnings and errors land in the API's logs, next to the backend's.</summary>
public sealed class ClientLogsEndpointTests(ApiFixture api) : ApiTest(api)
{
    private const string Path = "/api/client-logs";
    private const string Firefox = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0";

    [Fact]
    public async Task Post_WithAWarningAndAnError_LogsEachAtItsLevel()
    {
        var userId = Guid.CreateVersion7();
        var batch = Guid.NewGuid().ToString();

        var response = await PostAs(userId, new
        {
            entries = new object[]
            {
                new { level = "warn", message = $"Slow board {batch}", url = "https://kanabol.local/projects/1" },
                new
                {
                    level = "error",
                    message = $"Save failed {batch}",
                    url = "https://kanabol.local/users",
                    correlationId = "b7e1c2d4-0000-4000-8000-00000a1b2c3d",
                    details = "HttpErrorResponse: 500",
                },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var logged = Logged(batch);
        logged.Should().HaveCount(2);

        var warning = logged.Single(entry => Text(entry, "ClientMessage").StartsWith("Slow", StringComparison.Ordinal));
        warning.Level.Should().Be(LogEventLevel.Warning);
        Text(warning, "ClientUrl").Should().Be("https://kanabol.local/projects/1");

        var error = logged.Single(entry => Text(entry, "ClientMessage").StartsWith("Save", StringComparison.Ordinal));
        error.Level.Should().Be(LogEventLevel.Error);
        Text(error, "RequestCorrelationId").Should().Be("b7e1c2d4-0000-4000-8000-00000a1b2c3d");
        Text(error, "ClientDetails").Should().Be("HttpErrorResponse: 500");
        Text(error, "UserAgent").Should().Be(Firefox);
        error.Properties["UserId"].ToString().Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task Post_WhenTheBrowserDroppedEntries_LogsHowMany()
    {
        var userId = Guid.CreateVersion7();
        var batch = Guid.NewGuid().ToString();

        await PostAs(userId, new { entries = new[] { new { level = "error", message = batch } }, dropped = 7 });

        var dropped = Api.Factory.Logs.Events.Where(entry =>
                entry.MessageTemplate.Text.Contains("dropped", StringComparison.Ordinal)
                && entry.Properties.TryGetValue("UserId", out var user)
                && user.ToString().Contains(userId.ToString(), StringComparison.Ordinal))
            .Should().ContainSingle().Subject;
        dropped.Level.Should().Be(LogEventLevel.Warning);
        ((ScalarValue)dropped.Properties["DroppedCount"]).Value.Should().Be(7);
    }

    [Fact]
    public async Task Post_WithoutAToken_Returns401AndLogsNothing()
    {
        var batch = Guid.NewGuid().ToString();
        using var client = Api.Factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { entries = new[] { new { level = "error", message = batch } } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Logged(batch).Should().BeEmpty();
    }

    [Fact]
    public async Task Post_WithMoreThanTenEntries_Returns400AndLogsNothing()
    {
        var batch = Guid.NewGuid().ToString();
        var entries = Enumerable.Range(0, 11).Select(_ => new { level = "warn", message = batch }).ToArray();

        var response = await PostAs(Guid.CreateVersion7(), new { entries });

        await ShouldRefuse(response, "entries");
        Logged(batch).Should().BeEmpty();
    }

    [Fact]
    public async Task Post_WithNoEntries_Returns400()
    {
        var response = await PostAs(Guid.CreateVersion7(), new { entries = Array.Empty<object>() });

        await ShouldRefuse(response, "entries");
    }

    [Theory]
    [InlineData("info")]
    [InlineData("debug")]
    [InlineData("")]
    public async Task Post_WithALevelOtherThanWarnOrError_Returns400(string level)
    {
        var response = await PostAs(Guid.CreateVersion7(), new { entries = new[] { new { level, message = "x" } } });

        await ShouldRefuse(response, "entries[0].level");
    }

    [Fact]
    public async Task Post_WithAMessageOverItsLimit_Returns400()
    {
        var response = await PostAs(Guid.CreateVersion7(), new
        {
            entries = new[] { new { level = "error", message = new string('x', 4001) } },
        });

        await ShouldRefuse(response, "entries[0].message");
    }

    [Fact]
    public async Task Post_WithDetailsOverTheirLimit_Returns400()
    {
        var response = await PostAs(Guid.CreateVersion7(), new
        {
            entries = new[] { new { level = "error", message = "x", details = new string('x', 16001) } },
        });

        await ShouldRefuse(response, "entries[0].details");
    }

    private static async Task ShouldRefuse(HttpResponseMessage response, string field)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").EnumerateObject().Select(error => error.Name)
            .Should().Contain(name => name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> PostAs(Guid userId, object body)
    {
        using var client = Api.Factory.CreateClient(userId);
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("User-Agent", Firefox);
        return await client.SendAsync(request);
    }

    private List<LogEvent> Logged(string batch) =>
        Api.Factory.Logs.Events
            .Where(entry => entry.Properties.TryGetValue("ClientMessage", out var message)
                && message.ToString().Contains(batch, StringComparison.Ordinal))
            .ToList();

    private static string Text(LogEvent logEvent, string property) =>
        ((ScalarValue)logEvent.Properties[property]).Value as string ?? "";
}
