using Kanabol.Application.Abstractions;
using Kanabol.Domain.Common;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kanabol.Infrastructure.Persistence.Interceptors;

public sealed class AuditingInterceptor(IClock clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// A change to a project's members or columns is a change to the project, which then gets saved too,
    /// and so has its version checked. Two requests that each take away one of the last two owners both
    /// see the other owner stay, so without this both would save, and leave the project with none; two
    /// columns added at once would both take the last place.
    /// </summary>
    private static void TouchProjectsWithChangedParts(DbContext context)
    {
        var changedProjectIds = context.ChangeTracker.Entries<ProjectMember>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.ProjectId)
            .Concat(context.ChangeTracker.Entries<BoardColumn>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity.ProjectId))
            .ToHashSet();

        var unchangedProjects = context.ChangeTracker.Entries<Project>()
            .Where(entry => entry.State == EntityState.Unchanged && changedProjectIds.Contains(entry.Entity.Id));
        foreach (var project in unchangedProjects)
        {
            project.Property(entity => entity.UpdatedAt).IsModified = true;
        }
    }

    /// <summary>
    /// A change of a task's assignees changes no column of task_items, yet it is a change of the task: saved
    /// too, the task has its version checked, so of two changes at once the second gets a 409 instead of
    /// adding its people to the first one's, and the list shows when it was last changed.
    /// </summary>
    private static void TouchTasksWithChangedAssignees(DbContext context)
    {
        var changedTaskIds = context.ChangeTracker.Entries<TaskAssignee>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.TaskId)
            .ToHashSet();

        var unchangedTasks = context.ChangeTracker.Entries<TaskItem>()
            .Where(entry => entry.State == EntityState.Unchanged && changedTaskIds.Contains(entry.Entity.Id));
        foreach (var task in unchangedTasks)
        {
            task.Property(entity => entity.UpdatedAt).IsModified = true;
        }
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        TouchProjectsWithChangedParts(context);
        TouchTasksWithChangedAssignees(context);

        var now = clock.UtcNow;
        Guid? userId = currentUser.IsAuthenticated ? currentUser.Id : null;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedById = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedById = userId;
                entry.Property(e => e.CreatedAt).IsModified = false;
                entry.Property(e => e.CreatedById).IsModified = false;
            }
        }
    }
}
