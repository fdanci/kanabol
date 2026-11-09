using Kanabol.Application.Abstractions;
using Kanabol.Domain.Common;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kanabol.Infrastructure.Persistence;

/// <summary>
/// The project's counter moves in the database, never through the project's entity, so its xmin isn't
/// involved: a creation clashes neither with another one nor with a change to the members (SPEC section 5.3).
/// The transaction runs through the execution strategy, which refuses one opened outside it, and which runs
/// all of it again when the connection is lost on the way (SPEC section 5.3, group 20).
/// </summary>
public sealed class TaskNumbers(AppDbContext db) : ITaskNumbers
{
    public async Task<TaskItem?> SaveNewTaskAsync(Guid projectId, Func<int, Task<TaskItem>> create, CancellationToken ct)
    {
        var before = db.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        TaskItem? attempt = null;

        var task = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            projectId,
            async (_, id, token) =>
            {
                // A run that failed rolled its task back: it goes, with its events, so nobody hears of it.
                ForgetWhatIsNewSince(before);
                attempt = null;

                await using var transaction = await db.Database.BeginTransactionAsync(token);

                // Not composed with anything, so EF sends it as it is: an UPDATE can't sit inside a SELECT.
                var taken = await db.Database.SqlQuery<int>($"""
                    UPDATE projects SET next_task_number = next_task_number + 1
                    WHERE id = {id} AND archived_at IS NULL
                    RETURNING next_task_number - 1 AS "Value"
                    """).ToListAsync(token);
                if (taken.Count == 0)
                {
                    return null;
                }

                attempt = await create(taken[0]);
                db.TaskItems.Add(attempt);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return attempt;
            },

            // A commit whose answer was lost may have gone through: then the task is there, and nothing runs again.
            async (_, _, token) => attempt is not null
                && await db.TaskItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(saved => saved.Id == attempt.Id, token)
                    ? new ExecutionResult<TaskItem?>(true, attempt)
                    : new ExecutionResult<TaskItem?>(false, null),
            ct);

        if (task is not null)
        {
            // DomainEventsInterceptor held the events back while the transaction was open. Nothing is left to
            // write, so this save only sends them out, now that the task is committed.
            await db.SaveChangesAsync(ct);
        }

        return task;
    }

    private void ForgetWhatIsNewSince(HashSet<object> before)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(entry => !before.Contains(entry.Entity)).ToList())
        {
            (entry.Entity as Entity)?.ClearDomainEvents();
            entry.State = EntityState.Detached;
        }
    }
}
