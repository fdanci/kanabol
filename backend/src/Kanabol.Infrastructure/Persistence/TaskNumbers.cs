using Kanabol.Application.Abstractions;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Infrastructure.Persistence;

/// <summary>
/// The project's counter moves in the database, never through the project's entity, so its xmin isn't
/// involved: a creation clashes neither with another one nor with a change to the members (SPEC section 5.3).
/// </summary>
public sealed class TaskNumbers(AppDbContext db) : ITaskNumbers
{
    public async Task<TaskItem?> SaveNewTaskAsync(Guid projectId, Func<int, Task<TaskItem>> create, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Not composed with anything, so EF sends it as it is: an UPDATE can't sit inside a SELECT.
        var taken = await db.Database.SqlQuery<int>($"""
            UPDATE projects SET next_task_number = next_task_number + 1
            WHERE id = {projectId} AND archived_at IS NULL
            RETURNING next_task_number - 1 AS "Value"
            """).ToListAsync(ct);
        if (taken.Count == 0)
        {
            return null;
        }

        var task = await create(taken[0]);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // DomainEventsInterceptor held the events back while the transaction was open. Nothing is left to
        // write, so this save only sends them out, now that the task is committed.
        await db.SaveChangesAsync(ct);
        return task;
    }
}
