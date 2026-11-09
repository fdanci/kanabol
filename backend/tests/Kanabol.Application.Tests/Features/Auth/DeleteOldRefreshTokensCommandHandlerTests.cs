using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Auth.Commands.DeleteOldRefreshTokens;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Auth;

/// <summary>
/// The refresh tokens' cleanup (Tasks.md group 19; SPEC section 4.5): one DELETE takes the rows expired or
/// revoked more than 30 days ago, and says how many went. The rest stay, live or ended lately, so a stolen
/// token used again within those 30 days is still recognized and ends its family.
/// </summary>
public sealed class DeleteOldRefreshTokensCommandHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    // The spec's 30 days, written here.
    private static readonly TimeSpan _kept = TimeSpan.FromDays(30);

    private ICommandHandler<DeleteOldRefreshTokensCommand, int> Handler => Get<ICommandHandler<DeleteOldRefreshTokensCommand, int>>();

    private async Task<RefreshToken> Token(User user, string name, TimeSpan expiresIn, TimeSpan? revokedAgo = null)
    {
        var token = new RefreshToken(user.Id, Guid.CreateVersion7(), name, Now + expiresIn, Now - TimeSpan.FromDays(60));
        if (revokedAgo is { } ago)
        {
            token.Revoke(Now - ago);
        }

        await using var db = Postgres.CreateContext();
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<List<string>> HashesLeft()
    {
        await using var db = Postgres.CreateContext();
        return await db.RefreshTokens.Select(token => token.TokenHash).OrderBy(hash => hash).ToListAsync();
    }

    [Fact]
    public async Task Handle_DeletesWhatExpiredOrWasRevokedOverThirtyDaysAgo_AndKeepsTheRest()
    {
        var user = await CreateUser();
        await Token(user, "live", TimeSpan.FromDays(3));
        await Token(user, "expired-29-days-ago", -TimeSpan.FromDays(29));
        await Token(user, "expired-just-30-days-ago", -_kept);
        await Token(user, "expired-31-days-ago", -TimeSpan.FromDays(31));
        await Token(user, "revoked-29-days-ago", TimeSpan.FromDays(3), revokedAgo: TimeSpan.FromDays(29));
        await Token(user, "revoked-31-days-ago", TimeSpan.FromDays(3), revokedAgo: TimeSpan.FromDays(31));
        await Token(user, "expired-and-revoked-long-ago", -TimeSpan.FromDays(40), revokedAgo: TimeSpan.FromDays(45));

        var result = await Handler.Handle(new DeleteOldRefreshTokensCommand(), CancellationToken.None);

        result.Value.Should().Be(3);
        (await HashesLeft()).Should().Equal("expired-29-days-ago", "expired-just-30-days-ago", "live", "revoked-29-days-ago");
    }

    [Fact]
    public async Task Handle_WithNothingOld_DeletesNothing()
    {
        var user = await CreateUser();
        await Token(user, "live", TimeSpan.FromDays(7));

        (await Handler.Handle(new DeleteOldRefreshTokensCommand(), CancellationToken.None)).Value.Should().Be(0);
        (await HashesLeft()).Should().Equal("live");
    }

    [Fact]
    public async Task Handle_IsOneDeleteInTheDatabase_AndLogsHowManyWent()
    {
        var user = await CreateUser();
        await Token(user, "old", -TimeSpan.FromDays(31));
        await Token(user, "older", -TimeSpan.FromDays(90));

        await Handler.Handle(new DeleteOldRefreshTokensCommand(), CancellationToken.None);

        // EF writes every statement it sends as "Executed DbCommand".
        Logs.Entries.Where(entry => entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal)
                && entry.Message.Contains("refresh_tokens", StringComparison.Ordinal))
            .Should().ContainSingle("one statement touches the table").Which.Message.Should().Contain("DELETE FROM refresh_tokens");
        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information && entry.Message == "Deleted 2 refresh tokens expired or revoked over 30 days ago");
    }
}
