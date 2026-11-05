using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Auth.Commands.LogoutUser;

/// <summary>Ends this one session. A token it doesn't know is no error, since there's nothing left to end.</summary>
public sealed class LogoutUserCommandHandler(IAppDbContext db, ITokenService tokens, IClock clock) : ICommandHandler<LogoutUserCommand, Unit>
{
    public async Task<Result<Unit>> Handle(LogoutUserCommand command, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var familyId = await db.RefreshTokens
            .Where(token => token.TokenHash == hash)
            .Select(token => (Guid?)token.FamilyId)
            .FirstOrDefaultAsync(ct);

        if (familyId is not null)
        {
            await db.RefreshTokens.RevokeFamily(familyId.Value, clock.UtcNow, ct);
        }

        return Unit.Value;
    }
}
