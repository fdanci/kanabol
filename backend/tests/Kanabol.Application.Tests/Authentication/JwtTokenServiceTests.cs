using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kanabol.Application.Tests.Authentication;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private readonly JwtOptions _options = new()
    {
        Secret = new string('s', 48),
        Issuer = "kanabol-tests",
        Audience = "kanabol-tests-audience",
    };

    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        _clock.UtcNow.Returns(_now);
        _service = new JwtTokenService(Options.Create(_options), _clock);
    }

    [Fact]
    public void Options_ByDefault_AreWhatTheSpecAsks()
    {
        _options.AccessTokenMinutes.Should().Be(15);
        _options.RefreshTokenDays.Should().Be(7);
    }

    [Fact]
    public async Task CreateAccessToken_WhenGivenAUser_HasTheSubAndPermissionClaims()
    {
        var userId = Guid.CreateVersion7();

        var token = _service.CreateAccessToken(userId, ["users.manage", "reports.view"], mustChangePassword: false);

        var result = await Validate(token.Value, _options.Secret);
        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.FindFirst("sub")!.Value.Should().Be(userId.ToString());
        result.ClaimsIdentity.FindAll("permission").Select(claim => claim.Value)
            .Should().BeEquivalentTo("users.manage", "reports.view");
    }

    [Fact]
    public async Task CreateAccessToken_WhenThereIsOnePermission_StillReadsAsAPermissionClaim()
    {
        var token = _service.CreateAccessToken(Guid.CreateVersion7(), ["users.manage"], mustChangePassword: false);

        var result = await Validate(token.Value, _options.Secret);
        result.ClaimsIdentity.FindAll("permission").Select(claim => claim.Value).Should().ContainSingle("users.manage");
    }

    [Fact]
    public void CreateAccessToken_WhenCreated_ExpiresAfterTheConfiguredMinutes()
    {
        var token = _service.CreateAccessToken(Guid.CreateVersion7(), [], mustChangePassword: false);

        token.ExpiresAt.Should().Be(_now.AddMinutes(15));
        new JsonWebToken(token.Value).ValidTo.Should().BeCloseTo(_now.AddMinutes(15).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAccessToken_WhenThePasswordMustChange_SaysSo()
    {
        var token = _service.CreateAccessToken(Guid.CreateVersion7(), [], mustChangePassword: true);

        var result = await Validate(token.Value, _options.Secret);
        result.ClaimsIdentity.HasClaim(AuthClaims.MustChangePassword, "true").Should().BeTrue();
    }

    [Fact]
    public async Task CreateAccessToken_WhenThePasswordIsFine_LeavesTheClaimOut()
    {
        var token = _service.CreateAccessToken(Guid.CreateVersion7(), [], mustChangePassword: false);

        var result = await Validate(token.Value, _options.Secret);
        result.ClaimsIdentity.FindFirst(AuthClaims.MustChangePassword).Should().BeNull();
    }

    [Fact]
    public async Task CreateAccessToken_WhenCheckedWithAnotherSecret_IsInvalid()
    {
        var token = _service.CreateAccessToken(Guid.CreateVersion7(), [], mustChangePassword: false);

        var result = await Validate(token.Value, new string('x', 48));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateRefreshToken_WhenCreated_HasAHashAndExpiresAfterTheConfiguredDays()
    {
        var token = _service.CreateRefreshToken();

        token.Hash.Should().Be(_service.HashRefreshToken(token.Value));
        token.Hash.Should().NotContain(token.Value);
        token.ExpiresAt.Should().Be(_now.AddDays(7));
    }

    [Fact]
    public void CreateRefreshToken_WhenCreated_HasAtLeast256RandomBits()
    {
        var token = _service.CreateRefreshToken();

        Base64UrlEncoder.DecodeBytes(token.Value).Should().HaveCount(32);
        _service.CreateRefreshToken().Value.Should().NotBe(token.Value);
    }

    [Fact]
    public void HashRefreshToken_WhenGivenAToken_IsItsSha256InLowercaseHex() =>
        _service.HashRefreshToken("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    private async Task<TokenValidationResult> Validate(string token, string secret) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        });
}
