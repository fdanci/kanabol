using System.Net;
using System.Text;
using Kanabol.Domain.Authorization;
using Kanabol.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task Reset_BeforeEachTest_KeepsTheSeed()
    {
        await using var db = Api.Postgres.CreateContext();

        (await db.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
        (await db.Roles.SingleAsync()).Name.Should().Be(Role.AdminName);
        (await db.Users.SingleAsync()).MustChangePassword.Should().BeTrue();
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

    private async Task<TokenValidationResult> ValidateAsync(string token, string secret) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        });
}
