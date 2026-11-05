using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Kanabol.Application.Tests.Authentication;

public sealed class PasswordHasherTests
{
    private const string Password = "corect cal baterie capsa";

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_WhenGivenAPassword_DoesNotContainIt() =>
        _hasher.Hash(Password).Should().NotContain(Password);

    [Fact]
    public void Hash_WhenCalledTwice_SaltsEachOne() =>
        _hasher.Hash(Password).Should().NotBe(_hasher.Hash(Password));

    [Fact]
    public void Hash_WhenGivenAPassword_IsWhatPasswordHasherOfUserAccepts()
    {
        var verdict = new PasswordHasher<User>().VerifyHashedPassword(null!, _hasher.Hash(Password), Password);

        verdict.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_WhenThePasswordMatches_ReturnsTrue() =>
        _hasher.Verify(_hasher.Hash(Password), Password).Should().BeTrue();

    [Fact]
    public void Verify_WhenThePasswordDiffers_ReturnsFalse() =>
        _hasher.Verify(_hasher.Hash(Password), "alta parola complet").Should().BeFalse();

    [Fact]
    public void Verify_WhenOnlyTheCaseDiffers_ReturnsFalse() =>
        _hasher.Verify(_hasher.Hash(Password), Password.ToUpperInvariant()).Should().BeFalse();
}
