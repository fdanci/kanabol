using Kanabol.Infrastructure.Authentication;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Auth;

/// <summary>
/// The tests' common password is hashed once per suite (Tasks.md group 19; SPEC section 14.6): every hash has
/// a salt of its own, so people sharing one hash means it was made once; and it's the real hasher's kind.
/// </summary>
public sealed class CommonPasswordTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    [Fact]
    public async Task People_MadeByTheTests_ShareOneHashOfTheCommonPassword()
    {
        var ana = await CreateUser("ana@example.com");
        var dan = await CreateUser("dan@example.com", mustChangePassword: true);
        var admin = await CreateAdmin();

        dan.PasswordHash.Should().Be(ana.PasswordHash);
        admin.PasswordHash.Should().Be(ana.PasswordHash);
        new PasswordHasher().Verify(ana.PasswordHash, Password).Should().BeTrue();
    }
}
