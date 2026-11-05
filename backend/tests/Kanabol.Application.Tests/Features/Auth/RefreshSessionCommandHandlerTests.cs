using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Auth.Commands.RefreshSession;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class RefreshSessionCommandHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private ICommandHandler<RefreshSessionCommand, AuthSession> Handler => Get<ICommandHandler<RefreshSessionCommand, AuthSession>>();

    [Fact]
    public async Task Handle_WhenTheTokenIsValid_RotatesItInsideItsFamily()
    {
        var user = await CreateUser();
        var session = await Login(user);

        Clock.UtcNow.Returns(Now.AddMinutes(20));
        var result = await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RefreshToken.Value.Should().NotBe(session.RefreshToken.Value);
        result.Value.RefreshToken.ExpiresAt.Should().Be(Now.AddMinutes(20).AddDays(7));
        new JsonWebToken(result.Value.AccessToken.Value).GetClaim("sub").Value.Should().Be(user.Id.ToString());

        var tokens = await RefreshTokensOf(user);
        tokens.Should().HaveCount(2);
        tokens.Select(token => token.FamilyId).Distinct().Should().ContainSingle();
        tokens.Single(token => token.TokenHash == session.RefreshToken.Hash).RevokedAt.Should().Be(Now.AddMinutes(20));
        tokens.Single(token => token.TokenHash == result.Value.RefreshToken.Hash).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTheRoleChangedMeanwhile_PutsTheCurrentPermissionsInTheToken()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage]);
        var session = await Login(user);
        await using (var db = Postgres.CreateContext())
        {
            var role = await db.Roles.Include(existing => existing.RolePermissions).SingleAsync(existing => existing.Id == user.RoleId);
            var added = new Permission(Permissions.ReportsView);
            db.Permissions.Add(added);
            role.Grant(added.Id);
            await db.SaveChangesAsync();
        }

        var result = await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        new JsonWebToken(result.Value.AccessToken.Value).Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value)
            .Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
    }

    [Fact]
    public async Task Handle_WhenTheTokenIsUnknown_Refuses()
    {
        var user = await CreateUser();
        await Login(user);

        var result = await Handler.Handle(new RefreshSessionCommand("made-up"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await RefreshTokensOf(user)).Should().ContainSingle().Which.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenThereIsNoToken_Refuses()
    {
        var result = await Handler.Handle(new RefreshSessionCommand(""), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Handle_WhenTheTokenHasExpired_RefusesWithoutANewOne()
    {
        var user = await CreateUser();
        var session = await Login(user);

        Clock.UtcNow.Returns(Now.AddDays(7));
        var result = await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await RefreshTokensOf(user)).Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenAnOldTokenIsReused_RevokesTheWholeFamily()
    {
        var user = await CreateUser();
        var first = await Login(user);
        var second = await Handler.Handle(new RefreshSessionCommand(first.RefreshToken.Value), CancellationToken.None);

        Clock.UtcNow.Returns(Now.AddMinutes(1));
        var reused = await Handler.Handle(new RefreshSessionCommand(first.RefreshToken.Value), CancellationToken.None);

        reused.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await RefreshTokensOf(user)).Should().OnlyContain(token => token.IsRevoked);

        var afterwards = await Handler.Handle(new RefreshSessionCommand(second.Value.RefreshToken.Value), CancellationToken.None);
        afterwards.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Handle_WhenAnOldTokenIsReused_LeavesTheUsersOtherSessionsAlone()
    {
        var user = await CreateUser();
        var stolen = await Login(user);
        var otherDevice = await Login(user);
        await Handler.Handle(new RefreshSessionCommand(stolen.RefreshToken.Value), CancellationToken.None);

        await Handler.Handle(new RefreshSessionCommand(stolen.RefreshToken.Value), CancellationToken.None);

        var result = await Handler.Handle(new RefreshSessionCommand(otherDevice.RefreshToken.Value), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAnOldTokenIsReused_LogsAWarningWithoutTheToken()
    {
        var user = await CreateUser();
        var session = await Login(user);
        await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(user.Id.ToString()));
        Logs.Entries.Should().NotContain(entry => entry.Message.Contains(session.RefreshToken.Value));
        Logs.Entries.Should().NotContain(entry => entry.Message.Contains(session.RefreshToken.Hash));
    }

    [Fact]
    public async Task Handle_WhenTheSameTokenArrivesManyTimesAtOnce_LeavesNoLiveTokenInTheFamily()
    {
        var user = await CreateUser();
        var session = await Login(user);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RefreshInItsOwnScope(session.RefreshToken.Value)));

        results.Count(succeeded => succeeded).Should().BeLessThanOrEqualTo(1, "a token can only be rotated once");
        (await RefreshTokensOf(user)).Should().OnlyContain(token => token.IsRevoked);
    }

    [Fact]
    public async Task Handle_WhenTheUserIsDeactivated_Refuses()
    {
        var user = await CreateUser();
        var session = await Login(user);
        await Deactivate(user);

        var result = await Handler.Handle(new RefreshSessionCommand(session.RefreshToken.Value), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await RefreshTokensOf(user)).Should().ContainSingle();
    }

    private async Task<bool> RefreshInItsOwnScope(string token)
    {
        await Task.Yield();
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshSessionCommand, AuthSession>>();
        return (await handler.Handle(new RefreshSessionCommand(token), CancellationToken.None)).IsSuccess;
    }

    private async Task<AuthSession> Login(User user) =>
        (await Get<ICommandHandler<LoginUserCommand, AuthSession>>()
            .Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None)).Value;
}
