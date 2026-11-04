using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Kanabol.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Api.Tests.Support;

/// <summary>The real API on the test container, with settings that only exist here.</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>A token like the ones the API will issue, signed with the settings it's running with.</summary>
    public string CreateToken(Guid userId, params string[] permissions)
    {
        var jwt = Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var claims = permissions.Select(permission => new Claim("permission", permission)).Prepend(new Claim("sub", userId.ToString()));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(jwt.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    public HttpClient CreateClient(Guid userId, params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, permissions));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Secret", "only-for-these-tests-and-long-enough-to-pass");
        builder.UseSetting("Storage:Root", Path.Combine(Path.GetTempPath(), "kanabol-tests", "attachments"));
        builder.UseSetting("Admin:InitialPassword", "only-for-these-tests");
    }
}
