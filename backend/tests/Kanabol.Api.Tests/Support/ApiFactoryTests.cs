using System.Net;
using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Search;
using Kanabol.Domain.Authorization;
using Kanabol.Infrastructure.Configuration;
using Kanabol.Infrastructure.Embeddings;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Api.Tests.Support;

public sealed class ApiFactoryTests(ApiFixture api) : ApiTest(api)
{
    private JwtOptions JwtOptions => Api.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    [Fact]
    public async Task Health_WhenTheApiIsUp_Returns200()
    {
        using var client = Api.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Api_WhenStarted_UsesTheContainersDatabase()
    {
        var database = Api.Factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        database.ConnectionString.Should().Be(Api.Postgres.ConnectionString);
    }

    /// <summary>SPEC section 10: the default list, one value with commas in appsettings.json.</summary>
    [Fact]
    public void Storage_ByDefault_AllowsTheExtensionsOfTheSpec()
    {
        Api.Factory.Services.GetRequiredService<IAttachmentRules>().AllowedExtensions
            .Should().Equal("pdf", "png", "jpg", "jpeg", "gif", "txt", "md", "csv", "xlsx", "docx", "zip");
    }

    /// <summary>
    /// SPEC section 4.3: the overdue check is off in these tests, which run NotifyOverdueTasksCommand
    /// themselves, so no run of the worker lands in the middle of one; the rest is appsettings.json's.
    /// </summary>
    [Fact]
    public void Alerts_InTheseTests_AreOffWithTheDefaultInterval()
    {
        var alerts = Api.Factory.Services.GetRequiredService<IOptions<AlertsOptions>>().Value;

        alerts.OverdueCheckEnabled.Should().BeFalse();
        alerts.OverdueCheckInterval.Should().Be(TimeSpan.FromMinutes(15));
    }

    /// <summary>The refresh tokens' cleanup is off too: tests make old rows of their own (SPEC section 4.5).</summary>
    [Fact]
    public void RefreshTokenCleanup_InTheseTests_IsOffWithTheDefaultInterval()
    {
        var auth = Api.Factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        auth.RefreshTokenCleanupEnabled.Should().BeFalse();
        auth.RefreshTokenCleanupInterval.Should().Be(TimeSpan.FromDays(1));
    }

    /// <summary>SPEC section 19.4: the vectorization is off here, as by default, so no model is ever called.</summary>
    [Fact]
    public void Embeddings_InTheseTests_AreOff()
    {
        Api.Factory.Services.GetRequiredService<IOptions<EmbeddingsOptions>>().Value.Enabled.Should().BeFalse();
        Api.Factory.Services.GetRequiredService<IEmbeddingModel>().Should().BeOfType<TurnedOffEmbeddingModel>();
    }

    /// <summary>The search's API: the vectorization on, with the test's model, and no worker making vectors meanwhile.</summary>
    [Fact]
    public void Searching_HasTheVectorizationOn_WithTheTestsModel_AndNoWorker()
    {
        var services = Api.Searching.Services;
        using var scope = services.CreateScope();

        services.GetRequiredService<IOptions<EmbeddingsOptions>>().Value.Enabled.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<IEmbeddingModel>().Should().BeSameAs(Api.EmbeddingModel);
        services.GetServices<IHostedService>().Should().NotContain(service => service is EmbeddingsWorker)
            .And.Contain(service => service is DatabaseInitializer);
    }

    // Each API keeps its own logger: with the static one, the API started last took the other's logs.
    [Fact]
    public async Task Logs_OfEachApi_StayInItsOwnSink_WithBothRunning()
    {
        _ = Api.Searching.Services;
        var marker = $"/api/marker-{Guid.NewGuid():N}";
        using var client = Api.Factory.CreateClient();

        await client.GetAsync(new Uri(marker, UriKind.Relative));

        bool Mentions(Serilog.Events.LogEvent logEvent) =>
            logEvent.Properties.Values.Any(value => value.ToString().Contains(marker, StringComparison.Ordinal));
        Api.Factory.Logs.Events.Where(Mentions).Should().NotBeEmpty("the request was logged by the API that served it");
        Api.Searching.Logs.Events.Where(Mentions).Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_BeforeEachTest_KeepsTheSeed()
    {
        await using var db = Api.Postgres.CreateContext();

        (await db.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
        (await db.Roles.SingleAsync()).Name.Should().Be(Role.AdminName);
        (await db.Users.SingleAsync()).MustChangePassword.Should().BeTrue();
    }

    // A database from before tasks.create existed, which is what the deployed one will be on its next start.
    [Fact]
    public async Task Seed_WhenAPermissionIsNewInTheCode_AddsItAndGrantsItToAdmin()
    {
        await using var db = Api.Postgres.CreateContext();
        await db.Permissions.Where(permission => permission.Name == "tasks.create").ExecuteDeleteAsync();
        await SeedAgain();

        (await db.Permissions.AnyAsync(permission => permission.Name == "tasks.create")).Should().BeTrue();
        var admin = await db.Roles.Include(role => role.RolePermissions).SingleAsync(role => role.Name == Role.AdminName);
        var grantedNames = await db.Permissions
            .Where(permission => admin.RolePermissions.Select(granted => granted.PermissionId).Contains(permission.Id))
            .Select(permission => permission.Name)
            .ToListAsync();
        grantedNames.Should().Contain("tasks.create");
    }

    [Fact]
    public async Task CreateToken_WhenGivenAUser_IsValidForTheApisSettings()
    {
        var userId = Guid.CreateVersion7();

        var token = Api.Factory.CreateToken(userId, Permissions.UsersManage, Permissions.ReportsView);

        var result = await ValidateAsync(token, JwtOptions.Secret);
        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.FindFirst("sub")!.Value.Should().Be(userId.ToString());
        result.ClaimsIdentity.FindAll("permission").Select(claim => claim.Value)
            .Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
    }

    [Fact]
    public async Task CreateToken_WhenCheckedWithAnotherSecret_IsInvalid()
    {
        var token = Api.Factory.CreateToken(Guid.CreateVersion7());

        var result = await ValidateAsync(token, new string('x', 48));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task CreateClient_WhenGivenAUser_SendsItsToken()
    {
        var userId = Guid.CreateVersion7();

        using var client = Api.Factory.CreateClient(userId, Permissions.TasksArchive);

        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        var result = await ValidateAsync(client.DefaultRequestHeaders.Authorization.Parameter!, JwtOptions.Secret);
        result.ClaimsIdentity.FindFirst("sub")!.Value.Should().Be(userId.ToString());
    }

    private async Task SeedAgain()
    {
        await using var scope = Api.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().Seed(CancellationToken.None);
    }

    private async Task<TokenValidationResult> ValidateAsync(string token, string secret) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        });
}
