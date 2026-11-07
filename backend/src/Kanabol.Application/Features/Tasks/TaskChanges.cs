using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// What every change to a task goes through: an open task, a real member of its project, as for
/// creating one (SPEC section 4.4), and an open project. The entity writes the history; the save turns
/// a change made meanwhile into a 409, from the task's version (SPEC section 5.3).
/// </summary>
public sealed class TaskChanges(IAppDbContext db, IProjectAccess projectAccess, ICurrentUser currentUser, IClock clock)
{
    /// <summary>Loads the task, applies the change as the caller, now, saves it and reads it back.</summary>
    public async Task<Result<TaskItemDto>> Apply(
        Guid taskId,
        Action<TaskItem, Guid, DateTimeOffset> change,
        CancellationToken ct,
        Func<TaskItem, Task<Result>>? check = null)
    {
        var loaded = await LoadOpen(taskId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (check is not null)
        {
            var checkedOut = await check(loaded.Value);
            if (checkedOut.IsFailure)
            {
                return checkedOut.Error;
            }
        }

        change(loaded.Value, currentUser.Id, clock.UtcNow);

        var saved = await Save(ct);
        return saved.IsFailure ? saved.Error : await Read(taskId, ct);
    }

    /// <summary>An open task, for a real member of an open project.</summary>
    public async Task<Result<TaskItem>> LoadOpen(Guid taskId, CancellationToken ct)
    {
        var task = await db.TaskItems.FirstOrDefaultAsync(found => found.Id == taskId, ct);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        var allowed = await Allowed(task, projectAccess.EnsureRealMemberAsync, ct);
        return allowed.IsFailure ? allowed.Error : task;
    }

    /// <summary>
    /// The task to comment on or attach a file to: as LoadOpen, but an archived task, which the caller could
    /// see, answers 409 Task.Archived rather than 404 (SPEC section 4.4).
    /// </summary>
    public async Task<Result<TaskItem>> LoadToAddTo(Guid taskId, CancellationToken ct)
    {
        var task = await db.TaskItems.IgnoreQueryFilters().FirstOrDefaultAsync(found => found.Id == taskId, ct);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        var allowed = await Allowed(task, projectAccess.EnsureRealMemberAsync, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        return task.ArchivedAt is null ? task : TaskErrors.Archived;
    }

    /// <summary>
    /// A task archived or not, for whoever can see its project, in an open project: restoring needs
    /// tasks.archive on the endpoint, so an admin can restore without joining the project.
    /// </summary>
    public async Task<Result<TaskItem>> LoadForRestore(Guid taskId, CancellationToken ct)
    {
        var task = await db.TaskItems.IgnoreQueryFilters().FirstOrDefaultAsync(found => found.Id == taskId, ct);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        var allowed = await Allowed(task, projectAccess.EnsureMemberAsync, ct);
        return allowed.IsFailure ? allowed.Error : task;
    }

    public async Task<Result> Save(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskErrors.ChangedMeanwhile;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two changes at once that both add the same person: the second meets the first one's row in
            // task_assignees before it meets the task's version.
            return TaskErrors.ChangedMeanwhile;
        }
    }

    public async Task<Result<TaskItemDto>> Read(Guid taskId, CancellationToken ct) =>
        (await db.ReadTask(taskId, currentUser.Id, includeArchived: true, ct))!;

    private async Task<Result> Allowed(TaskItem task, Func<Guid, Guid, CancellationToken, Task<Result>> ensure, CancellationToken ct)
    {
        var access = await ensure(task.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var projectArchived = await db.Projects.IgnoreQueryFilters()
            .Where(project => project.Id == task.ProjectId)
            .Select(project => project.ArchivedAt != null)
            .FirstAsync(ct);
        return projectArchived ? ProjectErrors.Archived : Result.Success();
    }
}
