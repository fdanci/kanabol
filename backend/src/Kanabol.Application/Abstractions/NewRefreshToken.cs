namespace Kanabol.Application.Abstractions;

/// <summary>The value goes to the client once. Only the hash is ever stored.</summary>
public sealed record NewRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);
