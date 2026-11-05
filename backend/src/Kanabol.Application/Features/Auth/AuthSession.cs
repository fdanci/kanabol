using Kanabol.Application.Abstractions;

namespace Kanabol.Application.Features.Auth;

/// <summary>What the API hands out after a login or a refresh. It holds secrets, so it never gets logged.</summary>
public sealed record AuthSession(AccessToken AccessToken, NewRefreshToken RefreshToken, bool MustChangePassword);
