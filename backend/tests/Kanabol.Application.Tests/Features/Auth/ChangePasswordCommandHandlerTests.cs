using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.ChangePassword;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Auth.Commands.RefreshSession;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class ChangePasswordCommandHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private const string NewPassword = "alta parola complet noua";
    private const string WrongPassword = "nu este parola buna";

    private readonly HookedPasswordHasher _hasher = new();

    private ICommandHandler<ChangePasswordCommand, AuthSession> Handler => Get<ICommandHandler<ChangePasswordCommand, AuthSession>>();

    [Fact]
    public async Task Handle_WhenTheCurrentPasswordIsRight_ReplacesItAndClearsTheFlag()
    {
        var user = await CreateUser(mustChangePassword: true);
        SignIn(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await Reload(user);
        reloaded.MustChangePassword.Should().BeFalse();
        _hasher.Verify(reloaded.PasswordHash, NewPassword).Should().BeTrue();
        _hasher.Verify(reloaded.PasswordHash, Password).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_ReturnsASessionWithoutTheFlag()
    {
        var user = await CreateUser(mustChangePassword: true);
        SignIn(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        result.Value.MustChangePassword.Should().BeFalse();
        new JsonWebToken(result.Value.AccessToken.Value).TryGetClaim(AuthClaims.MustChangePassword, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_RevokesEveryEarlierSessionAndKeepsOnlyTheNewOne()
    {
        var user = await CreateUser();
        SignIn(user);
        var first = await Login(user);
        var second = await Login(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        var tokens = await RefreshTokensOf(user);
        tokens.Should().HaveCount(3);
        tokens.Where(token => token.IsRevoked).Select(token => token.TokenHash)
            .Should().BeEquivalentTo(first.RefreshToken.Hash, second.RefreshToken.Hash);
        tokens.Single(token => !token.IsRevoked).TokenHash.Should().Be(result.Value.RefreshToken.Hash);

        var refresh = Get<ICommandHandler<RefreshSessionCommand, AuthSession>>();
        (await refresh.Handle(new RefreshSessionCommand(result.Value.RefreshToken.Value), CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenSuccessful_LeavesOtherUsersSessionsAlone()
    {
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        await Login(other);
        SignIn(user);

        await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        (await RefreshTokensOf(other)).Should().ContainSingle().Which.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTheCurrentPasswordIsWrong_RefusesCountsTheFailureAndChangesNothing()
    {
        var user = await CreateUser();
        SignIn(user);
        await Login(user);

        var result = await Handler.Handle(new ChangePasswordCommand(WrongPassword, NewPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("CurrentPassword");
        var reloaded = await Reload(user);
        _hasher.Verify(reloaded.PasswordHash, Password).Should().BeTrue();
        reloaded.FailedLoginAttempts.Should().Be(1);
        (await RefreshTokensOf(user)).Should().ContainSingle().Which.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTenWrongPasswordsArriveAtOnce_CountsEveryOneAndLocks()
    {
        var user = await CreateUser();
        SignIn(user);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => FailInItsOwnScope()));

        var reloaded = await Reload(user);
        reloaded.LockedUntil.Should().Be(Now.AddMinutes(15), "a stolen access token mustn't get more guesses by sending them together");
        _hasher.Verify(reloaded.PasswordHash, Password).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AfterTenWrongPasswords_LocksTheAccountAndRefusesTheRightOne()
    {
        var user = await CreateUser();
        SignIn(user);
        for (var attempt = 0; attempt < User.MaxFailedLogins; attempt++)
        {
            await FailInItsOwnScope();
        }

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var reloaded = await Reload(user);
        reloaded.LockedUntil.Should().Be(Now.AddMinutes(15));
        _hasher.Verify(reloaded.PasswordHash, Password).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheNewPasswordIsTheCurrentOne_Refuses()
    {
        var user = await CreateUser(mustChangePassword: true);
        SignIn(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, Password), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("NewPassword");
        (await Reload(user)).MustChangePassword.Should().BeTrue();
    }

    [Theory]
    [InlineData("scurta")]
    [InlineData("password123")]
    public async Task Handle_WhenTheNewPasswordBreaksThePolicy_ReturnsTheFieldsError(string newPassword)
    {
        var user = await CreateUser();
        SignIn(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, newPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("NewPassword");
        _hasher.Verify((await Reload(user)).PasswordHash, Password).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheUserIsDeactivated_ReturnsUnauthorized()
    {
        var user = await CreateUser();
        SignIn(user);
        await Deactivate(user);

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenASessionRotatesAtTheSameTime_ChangesNothingAndReportsAConflict()
    {
        var user = await CreateUser();
        SignIn(user);
        var session = await Login(user);
        _hasher.BeforeNextHash = () =>
        {
            using var db = Postgres.CreateContext();
            db.RefreshTokens.Single(token => token.TokenHash == session.RefreshToken.Hash).Revoke(Now);
            db.SaveChanges();
        };

        var result = await Handler.Handle(new ChangePasswordCommand(Password, NewPassword), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionChangedMeanwhile);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await using var check = Postgres.CreateContext();
        _hasher.Verify((await check.Users.SingleAsync(existing => existing.Id == user.Id)).PasswordHash, Password).Should().BeTrue();
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IPasswordHasher>(_hasher);

    private async Task FailInItsOwnScope()
    {
        await Task.Yield();
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangePasswordCommand, AuthSession>>();
        await handler.Handle(new ChangePasswordCommand(WrongPassword, NewPassword), CancellationToken.None);
    }

    private void SignIn(User user)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.Id.Returns(user.Id);
    }

    private async Task<AuthSession> Login(User user)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<LoginUserCommand, AuthSession>>();
        return (await handler.Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None)).Value;
    }

    /// <summary>The real hasher, with a way to do something between the handler's read and its save.</summary>
    private sealed class HookedPasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();

        public Action? BeforeNextHash { get; set; }

        public string Hash(string password)
        {
            var hook = BeforeNextHash;
            BeforeNextHash = null;
            hook?.Invoke();
            return _inner.Hash(password);
        }

        public bool Verify(string hash, string password) => _inner.Verify(hash, password);
    }
}
