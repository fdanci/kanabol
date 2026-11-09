using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Auth.Commands.DeleteOldRefreshTokens;

/// <summary>
/// One run of the refresh tokens' cleanup (SPEC section 4.5), which RefreshTokensCleanupWorker sends at start
/// and then every Auth:RefreshTokenCleanupInterval, without a user. It answers how many rows it deleted.
/// </summary>
public sealed record DeleteOldRefreshTokensCommand : ICommand<int>;
