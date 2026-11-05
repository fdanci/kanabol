using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class LoginUserCommandHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private readonly CountingPasswordHasher _hasher = new();

    private ICommandHandler<LoginUserCommand, AuthSession> Handler => Get<ICommandHandler<LoginUserCommand, AuthSession>>();

    [Fact]
    public async Task Handle_WhenTheCredentialsAreRight_ReturnsASessionWithTheRolesPermissions()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage, Permissions.ReportsView]);
        await CreateUser("other@example.com", permissions: [Permissions.TasksArchive]);

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var jwt = new JsonWebToken(result.Value.AccessToken.Value);
        jwt.GetClaim("sub").Value.Should().Be(user.Id.ToString());
        jwt.Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value)
            .Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
        result.Value.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_StoresOnlyTheHashOfTheRefreshToken()
    {
        var user = await CreateUser();

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        var stored = (await RefreshTokensOf(user)).Should().ContainSingle().Subject;
        stored.TokenHash.Should().Be(Get<ITokenService>().HashRefreshToken(result.Value.RefreshToken.Value));
        stored.TokenHash.Should().NotBe(result.Value.RefreshToken.Value);
        stored.ExpiresAt.Should().Be(Now.AddDays(7));
        stored.CreatedAt.Should().Be(Now);
        stored.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenSignedInTwice_GivesEachSessionItsOwnFamily()
    {
        var user = await CreateUser();

        await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);
        await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        (await RefreshTokensOf(user)).Select(token => token.FamilyId).Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }

    [Fact]
    public async Task Handle_WhenThePasswordMustChange_SaysSo()
    {
        var user = await CreateUser(mustChangePassword: true);

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.Value.MustChangePassword.Should().BeTrue();
        new JsonWebToken(result.Value.AccessToken.Value).GetClaim(AuthClaims.MustChangePassword).Value.Should().Be("true");
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsInAnotherCase_SignsIn()
    {
        var user = await CreateUser("ana@example.com");

        var result = await Handler.Handle(new LoginUserCommand(" ANA@Example.com ", Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        new JsonWebToken(result.Value.AccessToken.Value).GetClaim("sub").Value.Should().Be(user.Id.ToString());
    }

    [Fact]
    public async Task Handle_WhenThePasswordIsWrong_RefusesAndCountsTheFailure()
    {
        var user = await CreateUser();

        var result = await Handler.Handle(new LoginUserCommand(user.Email, "alta parola complet"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        (await Reload(user)).FailedLoginAttempts.Should().Be(1);
        (await RefreshTokensOf(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsUnknown_RefusesLikeAWrongPasswordAfterAVerification()
    {
        var result = await Handler.Handle(new LoginUserCommand("nobody@example.com", Password), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        _hasher.Verifications.Should().Be(1, "without it, the response time would tell which emails exist");
    }

    [Fact]
    public async Task Handle_WhenTheUserIsDeactivated_RefusesAfterAVerification()
    {
        var user = await CreateUser();
        await Deactivate(user);

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        _hasher.Verifications.Should().Be(1);
        (await RefreshTokensOf(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AfterTenFailures_RefusesEvenTheRightPasswordFor15Minutes()
    {
        var user = await CreateUser();
        await FailLogins(user, 10);

        (await Reload(user)).LockedUntil.Should().Be(Now.AddMinutes(15));

        Clock.UtcNow.Returns(Now.AddMinutes(14));
        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        (await RefreshTokensOf(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AfterNineFailures_StillAcceptsTheRightPassword()
    {
        var user = await CreateUser();
        await FailLogins(user, 9);

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhileLocked_DoesNotExtendTheLockAndStillTakesAVerification()
    {
        var user = await CreateUser();
        await FailLogins(user, 10);
        var verificationsBefore = _hasher.Verifications;

        Clock.UtcNow.Returns(Now.AddMinutes(10));
        await Handler.Handle(new LoginUserCommand(user.Email, "alta parola complet"), CancellationToken.None);

        var reloaded = await Reload(user);
        reloaded.LockedUntil.Should().Be(Now.AddMinutes(15));
        reloaded.FailedLoginAttempts.Should().Be(0);
        _hasher.Verifications.Should().Be(verificationsBefore + 1);
    }

    [Fact]
    public async Task Handle_WhenTheLockHasExpired_SignsInAndClearsIt()
    {
        var user = await CreateUser();
        await FailLogins(user, 10);

        Clock.UtcNow.Returns(Now.AddMinutes(15));
        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await Reload(user)).LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_StartsCountingFailuresAgain()
    {
        var user = await CreateUser();
        await FailLogins(user, 9);

        await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);
        await FailLogins(user, 9);

        var reloaded = await Reload(user);
        reloaded.FailedLoginAttempts.Should().Be(9);
        reloaded.LockedUntil.Should().BeNull();
    }

    [Theory]
    [InlineData("", Password, "Email")]
    [InlineData("ana@example.com", "", "Password")]
    public async Task Handle_WhenAFieldIsEmpty_ReturnsTheFieldsError(string email, string password, string field)
    {
        var result = await Handler.Handle(new LoginUserCommand(email, password), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Handle_WhenSuccessful_LogsTheUserAndNoSecrets()
    {
        var user = await CreateUser();

        var result = await Handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None);

        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information && entry.Message.Contains(user.Id.ToString()));
        ShouldNotBeLogged(Password, result.Value.AccessToken.Value, result.Value.RefreshToken.Value, result.Value.RefreshToken.Hash);
    }

    [Fact]
    public async Task Handle_WhenRefused_LogsTheEmailAndNotThePassword()
    {
        var user = await CreateUser();

        await Handler.Handle(new LoginUserCommand(user.Email, "alta parola complet"), CancellationToken.None);

        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(user.Email));
        ShouldNotBeLogged("alta parola complet");
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IPasswordHasher>(_hasher);

    private async Task FailLogins(User user, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            await Handler.Handle(new LoginUserCommand(user.Email, "alta parola complet"), CancellationToken.None);
        }
    }

    private void ShouldNotBeLogged(params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Logs.Entries.Should().NotContain(entry => entry.Message.Contains(secret));
        }
    }

    /// <summary>The real hasher, counting how many verifications a refusal took.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();
        private int _verifications;

        public int Verifications => _verifications;

        public string Hash(string password) => _inner.Hash(password);

        public bool Verify(string hash, string password)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.Verify(hash, password);
        }
    }
}
