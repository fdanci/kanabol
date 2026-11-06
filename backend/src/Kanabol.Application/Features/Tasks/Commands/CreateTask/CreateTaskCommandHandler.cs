using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Tasks.Commands.CreateTask;

/// <summary>
/// tasks.create is checked on the endpoint, from the token, and a real membership here (SPEC section
/// 4.4). The number comes from the project's counter, saved with the task, so the project's version
/// guards it. When another change to the project lands first, the counter is read again and the task
/// takes the next number, up to three times: whoever creates a task edited nothing old, so they never
/// see a 409 for it (SPEC sections 5.1 and 5.3).
/// </summary>
public sealed class CreateTaskCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    IClock clock,
    ILogger<CreateTaskCommandHandler> logger) : ICommandHandler<CreateTaskCommand, TaskItemDto>
{
    public const int MaxRetries = 3;

    public async Task<Result<TaskItemDto>> Handle(CreateTaskCommand command, CancellationToken ct)
    {
        var project = await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(found => found.Id == command.ProjectId, ct);
        if (project is null)
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureRealMemberAsync(project.Id, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        if (project.ArchivedAt is not null)
        {
            return ProjectErrors.Archived;
        }

        var column = await db.BoardColumns
            .Where(found => found.ProjectId == project.Id && (command.ColumnId == null || found.Id == command.ColumnId))
            .OrderBy(found => found.Position)
            .FirstOrDefaultAsync(ct);
        if (column is null)
        {
            return TaskErrors.ColumnNotInProject;
        }

        if (command.AssigneeId is { } assigneeId && !await db.IsActiveMemberOf(project.Id, assigneeId, ct))
        {
            return TaskErrors.AssigneeNotAMember;
        }

        for (var retries = 0; ; retries++)
        {
            var created = await CreateIn(project, column, command, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Task {TaskId} created as {TaskKey} in project {ProjectId}",
                    created.Id,
                    TaskKey.Format(project.Key, created.Number),
                    project.Id);
                return (await db.ReadTask(created.Id, currentUser.Id, includeArchived: false, ct))!;
            }
            catch (DbUpdateException exception) when (retries < MaxRetries && TookTheNumberMeanwhile(exception))
            {
                // The project changed since it was read: another task took the number, or its members changed.
                db.TaskItems.Entry(created).State = EntityState.Detached;
                await db.Projects.Entry(project).ReloadAsync(ct);
                logger.LogInformation(
                    "Project {ProjectId} changed while task number {TaskNumber} was taken, trying again ({Retry} of {MaxRetries})",
                    project.Id,
                    created.Number,
                    retries + 1,
                    MaxRetries);
                if (project.ArchivedAt is not null)
                {
                    return ProjectErrors.Archived;
                }
            }
            catch (DbUpdateException exception) when (TookTheNumberMeanwhile(exception))
            {
                logger.LogWarning("Project {ProjectId} kept changing, so no task number could be taken", project.Id);
                return ProjectErrors.ChangedMeanwhile;
            }
        }
    }

    /// <summary>
    /// The project's version moved, or another task already has the number. The insert can reach
    /// ux_task_items_project_id_number before the update of the project reaches its version, and nothing
    /// else unique is written here: the task's id is new, and a new task has no history yet.
    /// </summary>
    private static bool TookTheNumberMeanwhile(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException || exception.IsUniqueViolation();

    private async Task<TaskItem> CreateIn(Project project, BoardColumn column, CreateTaskCommand command, CancellationToken ct)
    {
        var last = await db.TaskItems.Where(task => task.ColumnId == column.Id).MaxAsync(task => (int?)task.Position, ct);
        var task = TaskItem.Create(
            project,
            column,
            command.Title,
            command.Description,
            command.Priority,
            (last ?? -1) + 1,
            command.AssigneeId,
            command.DueDate,
            currentUser.Id,
            clock.UtcNow);
        db.TaskItems.Add(task);
        return task;
    }
}
