using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
