using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Kanabol.Api.Tests.Support;

/// <summary>Swagger only opens in Development, but the document it shows can be read from the services anywhere.</summary>
public sealed class SwaggerDocumentTests(ApiFixture api) : ApiTest(api)
{
    private OpenApiDocument Document => Api.Factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

    [Fact]
    public void Document_WhenGenerated_OffersABearerToken()
    {
        var scheme = Document.Components!.SecuritySchemes!["Bearer"];

        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    [Fact]
    public void Document_WhenGenerated_SendsTheTokenWithEveryRequest() =>
        Document.Security.Should().ContainSingle()
            .Which.Keys.Should().ContainSingle(scheme => scheme.Reference.Id == "Bearer");

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/users")]
    [InlineData("/api/users/{id}/deactivate")]
    [InlineData("/api/users/{id}/activate")]
    [InlineData("/api/users/{id}/reset-password")]
    [InlineData("/api/roles")]
    [InlineData("/api/permissions")]
    public void Document_WhenGenerated_ListsTheEndpoint(string path) =>
        Document.Paths.Should().ContainKey(path);
}
