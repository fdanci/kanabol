using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Support;

namespace Kanabol.Api.Tests.Templates;

/// <summary>The template for an endpoint's tests: real HTTP, the real pipeline, a real database.</summary>
public sealed class ProblemDetailsTests(ApiFixture api) : ApiTest(api)
{
    private const string CorrelationId = "from-the-test-1";

    [Fact]
    public async Task Get_WhenTheRouteDoesNotExist_Returns404ProblemDetails()
    {
        var (response, body) = await Send(HttpMethod.Get, "/api/nowhere");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ShouldBeProblemDetails(response, body, 404);
    }

    [Fact]
    public async Task Get_WhenTheResultIsNotFound_ReturnsTheErrorsCode()
    {
        var (response, body) = await Send(HttpMethod.Get, "/api/test-samples/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ShouldBeProblemDetails(response, body, 404);
        body.RootElement.GetProperty("code").GetString().Should().Be("Sample.NotFound");
        body.RootElement.GetProperty("detail").GetString().Should().Be("No such thing.");
    }

    [Fact]
    public async Task Post_WhenTheCommandIsInvalid_Returns400WithItsFields()
    {
        var (response, body) = await Send(HttpMethod.Post, "/api/test-samples/invalid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ShouldBeProblemDetails(response, body, 400);
        body.RootElement.GetProperty("errors").GetProperty("userName")[0].GetString().Should().Be("Required.");
    }

    [Fact]
    public async Task Get_WhenTheEndpointThrows_Returns500WithoutTheDetails()
    {
        var (response, body) = await Send(HttpMethod.Get, "/api/test-samples/boom");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ShouldBeProblemDetails(response, body, 500);
        body.RootElement.GetRawText().Should().NotContain("secret detail");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(CorrelationId);
    }

    private static void ShouldBeProblemDetails(HttpResponseMessage response, JsonDocument body, int status)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(status);
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(CorrelationId);
    }

    private async Task<(HttpResponseMessage Response, JsonDocument Body)> Send(HttpMethod method, string path)
    {
        using var client = Api.Factory.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", CorrelationId);

        var response = await client.SendAsync(request);
        return (response, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
    }
}
