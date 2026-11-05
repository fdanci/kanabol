using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Kanabol.Api.Tests.Support;
using Kanabol.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Api.Tests.Auth;

/// <summary>Which tokens the API takes, on an endpoint that says nothing about authentication.</summary>
public sealed class AccessTokenValidationTests(ApiFixture api) : AuthApiTest(api)
{
    private JwtOptions JwtOptions => Api.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    [Fact]
    public async Task Endpoint_WithoutAToken_Returns401ProblemDetails()
    {
        var response = await Client.GetAsync(new Uri("/api/test-protected", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Endpoint_WithATokenFromLogin_Returns200()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        (await GetWith(await AccessTokenFrom(login))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_WithTheTestFactorysToken_Returns200() =>
        (await GetWith(Api.Factory.CreateToken(Guid.CreateVersion7()))).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task Endpoint_WithAnExpiredToken_Returns401()
    {
        var token = CreateToken(descriptor =>
        {
            descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-20);
            descriptor.Expires = DateTime.UtcNow.AddSeconds(-1);
        });

        (await GetWith(token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoint_WithATokenSignedByAnotherKey_Returns401()
    {
        var token = CreateToken(descriptor => descriptor.SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 48))),
            SecurityAlgorithms.HmacSha256));

        (await GetWith(token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The test key is too short to sign with anything stronger, so a token can't show this one.
    [Fact]
    public void JwtBearer_WhenConfigured_AcceptsOnlyHs256()
    {
        var bearer = Api.Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.TokenValidationParameters.ValidAlgorithms.Should().ContainSingle(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public async Task Endpoint_WithAnUnsignedToken_Returns401()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(15),
        });

        (await GetWith(token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("another-issuer", null)]
    [InlineData(null, "another-audience")]
    public async Task Endpoint_WithAnotherIssuerOrAudience_Returns401(string? issuer, string? audience)
    {
        var token = CreateToken(descriptor =>
        {
            descriptor.Issuer = issuer ?? descriptor.Issuer;
            descriptor.Audience = audience ?? descriptor.Audience;
        });

        (await GetWith(token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private string CreateToken(Action<SecurityTokenDescriptor> change)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtOptions.Secret)),
                SecurityAlgorithms.HmacSha256),
        };
        change(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpResponseMessage> GetWith(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }
}
