using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Auth.Commands.LogoutUser;
using Kanabol.Application.Features.Auth.Commands.RefreshSession;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class LogoutUserCommandHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private ICommandHandler<LogoutUserCommand, Unit> Handler => Get<ICommandHandler<LogoutUserCommand, Unit>>();

    [Fact]
    public async Task Handle_WhenTheTokenIsValid_RevokesItsWholeFamily()
    {
        var user = await CreateUser();
        var first = await Login(user);
        var rotated = await Get<ICommandHandler<RefreshSessionCommand, AuthSession>>()
            .Handle(new RefreshSessionCommand(first.RefreshToken.Value), CancellationToken.None);

        var result = await Handler.Handle(new LogoutUserCommand(rotated.Value.RefreshToken.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await RefreshTokensOf(user)).Should().HaveCount(2).And.OnlyContain(token => token.IsRevoked);
    }

    [Fact]
    public async Task Handle_WhenTheUserHasOtherSessions_LeavesThemAlone()
    {
        var user = await CreateUser();
        var thisDevice = await Login(user);
        var otherDevice = await Login(user);

        await Handler.Handle(new LogoutUserCommand(thisDevice.RefreshToken.Value), CancellationToken.None);

        var tokens = await RefreshTokensOf(user);
        tokens.Single(token => token.TokenHash == thisDevice.RefreshToken.Hash).IsRevoked.Should().BeTrue();
        tokens.Single(token => token.TokenHash == otherDevice.RefreshToken.Hash).IsRevoked.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("made-up")]
    public async Task Handle_WhenTheTokenIsUnknown_StillSucceeds(string token)
    {
        var user = await CreateUser();
        await Login(user);

        var result = await Handler.Handle(new LogoutUserCommand(token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await RefreshTokensOf(user)).Should().ContainSingle().Which.IsRevoked.Should().BeFalse();
    }

    private async Task<AuthSession> Login(User user) =>
        (await Get<ICommandHandler<LoginUserCommand, AuthSession>>()
            .Handle(new LoginUserCommand(user.Email, Password), CancellationToken.None)).Value;
}
