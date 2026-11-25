using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Embeddings;
using Kanabol.Tests.Shared.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Serilog.Core;

namespace Kanabol.Api.Tests.Support;

/// <summary>
/// The real API on the test container, with settings that only exist here. With an embedding model, the
/// vectorization is on, with that model and without the worker (SPEC section 19.6), for the search's tests;
/// otherwise it's off, as by default.
/// </summary>
public sealed class ApiFactory(string connectionString, Func<IEmbeddingModel>? embeddingModel = null) : WebApplicationFactory<Program>
{
    /// <summary>What the API has logged, across every test that shares this factory.</summary>
    public RecordingLogSink Logs { get; } = new();

    /// <summary>A token like the ones the API will issue, signed with the settings it's running with.</summary>
    public string CreateToken(Guid userId, params string[] permissions) => CreateToken(userId, lifetime: null, permissions);

    /// <summary>The same, valid for the time given instead of the API's own, such as a token about to expire.</summary>
    public string CreateToken(Guid userId, TimeSpan? lifetime, params string[] permissions)
    {
        var jwt = Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var claims = permissions.Select(permission => new Claim("permission", permission)).Prepend(new Claim("sub", userId.ToString()));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(jwt.AccessTokenMinutes)),
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

        // The tests run the overdue check themselves, when they want it (SPEC section 4.3), and make old refresh
        // tokens of their own, which no cleanup may take meanwhile (SPEC section 4.5).
        builder.UseSetting("Alerts:OverdueCheckEnabled", "false");
        builder.UseSetting("Auth:RefreshTokenCleanupEnabled", "false");

        // Controllers that only exist in the tests, to reach paths no real endpoint has yet.
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
            services.AddSingleton<ILogEventSink>(Logs);
        });

        if (embeddingModel is not null)
        {
            // Checked at start once on, though the test's model replaces Azure's: none of them reaches anything.
            builder.UseSetting("Embeddings:Enabled", "true");
            builder.UseSetting("Embeddings:Model", FakeEmbeddingModel.DefaultName);
            builder.UseSetting("Embeddings:Azure:Endpoint", "https://embeddings.invalid/");
            builder.UseSetting("Embeddings:Azure:Deployment", "unused-in-tests");
            builder.UseSetting("Embeddings:Azure:ApiKey", "unused-in-tests");

            // After the API's own services: the model is the one the test has now, and only the test makes vectors.
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => embeddingModel());
                services.Remove(services.Single(service =>
                    service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(EmbeddingsWorker)));
            });
        }
    }
}
