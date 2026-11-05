namespace Kanabol.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
