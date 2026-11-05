using Kanabol.Application.Abstractions;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kanabol.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    // The user argument of PasswordHasher<T> is only there for custom hashers, and this one ignores it.
    private static readonly User _noUser = null!;

    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(_noUser, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(_noUser, hash, password) != PasswordVerificationResult.Failed;
}
