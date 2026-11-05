using Kanabol.Application.Abstractions;
using Kanabol.Application.Tests.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features;

/// <summary>For the handlers that manage users and roles, on a database with every permission, as after the seed.</summary>
public abstract class ManagementHandlerTest(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await using var db = Postgres.CreateContext();
        db.Permissions.AddRange(Permissions.All.Select(name => new Permission(name)));
        await db.SaveChangesAsync();
    }

    protected void SignIn(User user)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.Id.Returns(user.Id);
    }

    protected async Task<User> SignInWith(params string[] permissions)
    {
        var user = await CreateUser("manager@example.com", permissions: permissions);
        SignIn(user);
        return user;
    }

    /// <summary>The Admin role is created with its first holder, with every permission, like the seed does.</summary>
    protected async Task<User> CreateAdmin(string email = "admin@example.com")
    {
        await using var db = Postgres.CreateContext();

        var role = await db.Roles.SingleOrDefaultAsync(existing => existing.IsSystem);
        if (role is null)
        {
            role = new Role(Role.AdminName, isSystem: true);
            await db.Permissions.ForEachAsync(permission => role.Grant(permission.Id));
            db.Roles.Add(role);
        }

        var user = new UserBuilder().WithEmail(email).InRole(role).WithPasswordHash(Get<IPasswordHasher>().Hash(Password)).Build();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task<List<string>> PermissionNamesOf(Guid roleId)
    {
        await using var db = Postgres.CreateContext();
        return await db.Roles
            .Where(role => role.Id == roleId)
            .SelectMany(role => role.RolePermissions)
            .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name)
            .ToListAsync();
    }

    protected async Task<Role> CreateRole(string name, params string[] permissions)
    {
        await using var db = Postgres.CreateContext();

        var role = new Role(name, isSystem: false);
        var granted = await db.Permissions.Where(permission => permissions.Contains(permission.Name)).ToListAsync();
        granted.ForEach(permission => role.Grant(permission.Id));

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }
}
