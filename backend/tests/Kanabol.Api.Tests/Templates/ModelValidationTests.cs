using System.Net;
using System.Text;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Templates;

/// <summary>
/// What MVC refuses before a validator runs: a missing field, a value of the wrong type, a query that
/// isn't a number. It has to look like any other 400 (SPEC section 4.6): a code, the field as the
/// client named it, and nothing about the API's own types.
/// </summary>
public sealed class ModelValidationTests(ApiFixture api) : ManagementApiTest(api)
{
    [Fact]
    public async Task Post_WithAMissingField_Returns400WithTheCodeAndTheFieldInCamelCase()
    {
        var body = await Problem(await PostJson("/api/roles", """{ "name": "Analist" }"""));

        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").TryGetProperty("permissions", out _).Should().BeTrue();
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_WithAValueOfTheWrongType_NamesTheFieldAndNoType()
    {
        var response = await PostJson("/api/roles", """{ "name": "Analist", "permissions": "nu-e-lista" }""");
        var text = await response.Content.ReadAsStringAsync();

        using var body = await Problem(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").TryGetProperty("permissions", out _).Should().BeTrue();
        text.Should().NotContain("Kanabol.").And.NotContain("System.");
    }

    [Fact]
    public async Task Post_WithTextThatIsNotValidUnicode_SaysNothingAboutTheRequestType()
    {
        var response = await PostJson("/api/users", """{ "email": "a\ud800@example.com", "displayName": "Ana", "roleId": "019a0000-0000-7000-8000-000000000001", "initialPassword": "o parola initiala buna" }""");
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        text.Should().NotContain("CreateUserRequest").And.NotContain("Kanabol.");
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).Should().Equal("email");
    }

    [Fact]
    public async Task Login_WithoutAPassword_Returns400WithTheCode()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("""{ "email": "ana@example.com" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Forwarded-For", UnusedAddress());

        using var body = await Problem(await Client.SendAsync(request));

        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        body.RootElement.GetProperty("errors").TryGetProperty("password", out _).Should().BeTrue();
    }

    // The query says page, so the error does too, whichever check refused it.
    [Theory]
    [InlineData("/api/users?page=0")]
    [InlineData("/api/users?page=abc")]
    [InlineData("/api/roles?page=0")]
    public async Task Get_WithABadPage_ReportsItOnPage(string path)
    {
        var token = path.Contains("roles", StringComparison.Ordinal)
            ? await TokenWith(Permissions.RolesManage)
            : await TokenWith(Permissions.UsersManage);

        using var body = await Problem(await Send(HttpMethod.Get, path, token));

        body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).Should().Equal("page");
    }

    private static async Task<JsonDocument> Problem(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostJson(string path, string json)
    {
        var token = await AdminToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
}
