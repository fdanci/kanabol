using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Infrastructure.Persistence;

/// <summary>
/// Brings the permissions in line with the constants in code, and makes sure the Admin role has
/// all of them and that an admin exists. Safe to run on every start.
/// </summary>
public sealed class DatabaseSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<AdminOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    public async Task Seed(CancellationToken ct)
    {
        var permissions = await SyncPermissions(ct);
        var adminRole = await EnsureAdminRole(permissions, ct);
        await EnsureAdminUser(adminRole, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Permission>> SyncPermissions(CancellationToken ct)
    {
        var existing = await db.Permissions.ToListAsync(ct);
        db.Permissions.RemoveRange(existing.Where(permission => !Permissions.All.Contains(permission.Name)));

        var kept = existing.Where(permission => Permissions.All.Contains(permission.Name)).ToList();
        var added = Permissions.All
            .Except(kept.Select(permission => permission.Name))
            .Select(name => new Permission(name))
            .ToList();
        db.Permissions.AddRange(added);

        return [.. kept, .. added];
    }

    private async Task<Role> EnsureAdminRole(List<Permission> permissions, CancellationToken ct)
    {
        var role = await db.Roles
            .Include(candidate => candidate.RolePermissions)
            .SingleOrDefaultAsync(candidate => candidate.Name == Role.AdminName && candidate.IsSystem, ct);

        if (role is null)
        {
            role = new Role(Role.AdminName, isSystem: true);
            db.Roles.Add(role);
        }

        permissions.ForEach(permission => role.Grant(permission.Id));
        return role;
    }

    private async Task EnsureAdminUser(Role adminRole, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(user => user.RoleId == adminRole.Id, ct))
        {
            return;
        }

        var admin = options.Value;
        if (string.IsNullOrEmpty(admin.InitialPassword))
        {
            throw new InvalidOperationException(
                "No admin exists yet, so Admin:InitialPassword has to be set for the first start.");
        }

        db.Users.Add(new User(
            admin.Email,
            "Administrator",
            passwordHasher.Hash(admin.InitialPassword),
            adminRole.Id,
            mustChangePassword: true));

        logger.LogInformation("Creating the initial admin user");
    }
}
