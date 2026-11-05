using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Auth;

public static class UserQueryExtensions
{
    /// <summary>
    /// One statement that the database runs on the current row, and not a value read earlier: hashing
    /// a password takes long enough that guesses sent together would overwrite each other's count,
    /// and get far more than the 10 tries before the lock.
    /// </summary>
    public static Task<int> CountFailedLogin(this IQueryable<User> users, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset? lockedUntil = now + User.LockoutDuration;

        return users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(
                        user => user.LockedUntil,
                        user => user.FailedLoginAttempts + 1 >= User.MaxFailedLogins ? lockedUntil : user.LockedUntil)
                    .SetProperty(
                        user => user.FailedLoginAttempts,
                        user => user.FailedLoginAttempts + 1 >= User.MaxFailedLogins ? 0 : user.FailedLoginAttempts + 1),
                ct);
    }
}
