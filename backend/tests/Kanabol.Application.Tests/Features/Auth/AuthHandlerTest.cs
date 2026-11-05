using Kanabol.Application.Abstractions;
using Kanabol.Application.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Auth;

public abstract class AuthHandlerTest(PostgresFixture postgres) : HandlerTest(postgres)
{
    protected const string Password = "corect cal baterie capsa";

    protected static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow.Returns(Now);
    }

    protected async Task<User> CreateUser(string email = "ana@example.com", bool mustChangePassword = false, params string[] permissions)
    {
        await using var db = Postgres.CreateContext();

        var role = new Role($"Role of {email}", isSystem: false);
        foreach (var name in permissions)
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(existing => existing.Name == name);
            if (permission is null)
            {
                permission = new Permission(name);
                db.Permissions.Add(permission);
            }

            role.Grant(permission.Id);
        }

        var builder = new UserBuilder().WithEmail(email).InRole(role).WithPasswordHash(Get<IPasswordHasher>().Hash(Password));
        var user = (mustChangePassword ? builder.MustChangePassword() : builder).Build();

        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task Deactivate(User user)
    {
        await using var db = Postgres.CreateContext();
        await db.Users.Where(existing => existing.Id == user.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, false));
    }

    protected async Task<User> Reload(User user)
    {
        await using var db = Postgres.CreateContext();
        return await db.Users.SingleAsync(existing => existing.Id == user.Id);
    }

    protected async Task<List<RefreshToken>> RefreshTokensOf(User user)
    {
        await using var db = Postgres.CreateContext();
        return await db.RefreshTokens.Where(token => token.UserId == user.Id).ToListAsync();
    }
}
