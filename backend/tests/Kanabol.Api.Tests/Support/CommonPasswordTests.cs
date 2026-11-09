using System.Net;
using Kanabol.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Support;

/// <summary>
/// The tests' common password is hashed once per suite (Tasks.md group 19; SPEC section 14.6): PBKDF2 for
/// every person of every test was most of the merge into main's 180 seconds. Every hash has a salt of its
/// own, so people sharing one hash means it was made once; and it's still the API's own kind of hash.
/// </summary>
public sealed class CommonPasswordTests(ApiFixture api) : ManagementApiTest(api)
{
    [Fact]
    public async Task People_MadeByTheTests_ShareOneHashOfTheCommonPassword_AndSignInWithIt()
    {
        var ana = await CreateUser(UnusedEmail());
        var dan = await CreateUser(UnusedEmail(), mustChangePassword: true);
        var admin = await CreateAdmin();

        dan.PasswordHash.Should().Be(ana.PasswordHash);
        admin.PasswordHash.Should().Be(ana.PasswordHash);
        Api.Factory.Services.GetRequiredService<IPasswordHasher>().Verify(ana.PasswordHash, Password).Should().BeTrue();
        (await Login(ana.Email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
