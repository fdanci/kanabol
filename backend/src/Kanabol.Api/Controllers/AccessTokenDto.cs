namespace Kanabol.Api.Controllers;

public sealed record AccessTokenDto(string AccessToken, DateTimeOffset ExpiresAt, bool MustChangePassword);
