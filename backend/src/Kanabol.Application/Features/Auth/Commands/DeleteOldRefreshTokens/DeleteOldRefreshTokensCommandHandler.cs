using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth.Commands.DeleteOldRefreshTokens;

/// <summary>
/// Deletes, in one DELETE, the refresh tokens that expired or were revoked more than 30 days ago (SPEC
/// section 4.5). A token revoked lately stays, so using it again is still seen as theft and ends its family;
/// past 30 days it's an unknown token, which answers 401 all the same.
/// </summary>
public sealed class DeleteOldRefreshTokensCommandHandler(
    IAppDbContext db,
    IClock clock,
    ILogger<DeleteOldRefreshTokensCommandHandler> logger) : ICommandHandler<DeleteOldRefreshTokensCommand, int>
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    public async Task<Result<int>> Handle(DeleteOldRefreshTokensCommand command, CancellationToken ct)
    {
        var before = clock.UtcNow - KeptFor;

        var deleted = await db.RefreshTokens
            .Where(token => token.ExpiresAt < before || token.RevokedAt < before)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("Deleted {DeletedCount} refresh tokens expired or revoked over {KeptDays} days ago", deleted, KeptFor.Days);
        return deleted;
    }
}
