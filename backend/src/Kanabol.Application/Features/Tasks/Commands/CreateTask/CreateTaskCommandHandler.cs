using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Tasks.Commands.CreateTask;

/// <summary>
/// tasks.create is checked on the endpoint, from the token, and a real membership here (SPEC section
/// 4.4). The number comes from one UPDATE … RETURNING on the project's counter, in the transaction that
/// saves the task (ITaskNumbers), so any number of creations at once get different numbers, without a
/// retry and without a 409 (SPEC sections 5.1 and 5.3, decided 2026-11-06).
/// </summary>
public sealed class CreateTaskCommandHandler(
    IAppDbContext db,
    ITaskNumbers taskNumbers,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    IClock clock,
    ILogger<CreateTaskCommandHandler> logger) : ICommandHandler<CreateTaskCommand, TaskItemDto>
{
    public async Task<Result<TaskItemDto>> Handle(CreateTaskCommand command, CancellationToken ct)
    {
        // Not tracked: the counter moves in the database, which leaves a tracked copy with an old version
        // for whatever else the same scope saves.
        var project = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(found => found.Id == command.ProjectId, ct);
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

        if (!await db.AreAllActiveMembersOf(project.Id, command.AssigneeIds, ct))
        {
            return TaskErrors.AssigneeNotAMember;
        }

        var now = clock.UtcNow;
        TaskItem? created;
        try
        {
            // The end of the column is read once the number is taken: the creations before this one in the
            // project are committed by then, so two of them never end up in the same place.
            created = await taskNumbers.SaveNewTaskAsync(
                project.Id,
                async number =>
                {
                    var last = await db.TaskItems.Where(task => task.ColumnId == column.Id).MaxAsync(task => (int?)task.Position, ct);
                    return TaskItem.Create(
                        project,
                        column,
                        number,
                        command.Title,
                        command.Description,
                        command.Priority,
                        (last ?? -1) + 1,
                        command.AssigneeIds,
                        command.DueDate,
                        currentUser.Id,
                        now);
                },
                ct);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // The column was deleted after it was read, and the number with it, since nothing committed.
            return TaskErrors.ColumnNotInProject;
        }

        if (created is null)
        {
            // Archived after it was read above: the counter only moves in an open project.
            return ProjectErrors.Archived;
        }

        logger.LogInformation(
            "Task {TaskId} created as {TaskKey} in project {ProjectId}",
            created.Id,
            TaskKey.Format(project.Key, created.Number),
            project.Id);
        return (await db.ReadTask(created.Id, currentUser, includeArchived: false, ct))!;
    }
}
