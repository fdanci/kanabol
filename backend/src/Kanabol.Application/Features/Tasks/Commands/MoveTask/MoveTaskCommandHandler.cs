using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Commands.MoveTask;

/// <summary>
/// The task goes to its place and the open tasks around it move up or down, so the column it left and
/// the one it entered stay 0, 1, 2..., archived tasks taking no place. Every task whose place changes is
/// saved with its version, so of two moves through the same column at once the second gets 409
/// Task.ChangedMeanwhile and changes nothing (SPEC sections 5.3 and 6.3). Only the moved task writes
/// history, and only when its column changes. The checks go in the order of SPEC section 4.4: the request's
/// form, the access, a column on the board, then Task.CannotMoveBack, then what the column asks for (an
/// estimate to enter În lucru, a reason to enter În pauză), then the versions.
/// </summary>
public sealed class MoveTaskCommandHandler(
    IAppDbContext db,
    TaskChanges changes,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<MoveTaskCommand, TaskItemDto>
{
    public async Task<Result<TaskItemDto>> Handle(MoveTaskCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.TaskId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var task = loaded.Value;
        var board = await db.BoardColumns.Where(found => found.ProjectId == task.ProjectId).ToListAsync(ct);
        var column = board.Find(found => found.Id == command.ColumnId);
        if (column is null)
        {
            return TaskErrors.ColumnNotInProject;
        }

        if (BoardColumn.GoesBackBeforeInProgress(board.Single(found => found.Id == task.ColumnId), column, board))
        {
            return TaskErrors.CannotMoveBack;
        }

        var now = clock.UtcNow;
        if (column.Kind == ColumnKind.InProgress && column.Id != task.ColumnId)
        {
            if (command.EstimatedMinutes is { } minutes)
            {
                if (minutes is < TaskItem.MinEstimateMinutes or > TaskItem.MaxEstimateMinutes)
                {
                    return TaskErrors.EstimateOutOfLimits;
                }

                task.SetEstimate(minutes, currentUser.Id, now);
            }
            else if (task.EstimatedMinutes is null)
            {
                return TaskErrors.EstimateRequired;
            }
        }

        if (column.Kind == ColumnKind.Paused && column.Id != task.ColumnId && !TaskItem.IsAPauseReason(command.PauseReason))
        {
            return TaskErrors.PauseReasonRequired;
        }

        var neighbours = await db.TaskItems
            .Where(other => (other.ColumnId == task.ColumnId || other.ColumnId == column.Id) && other.Id != task.Id)
            .OrderBy(other => other.Position)
            .ThenBy(other => other.Number)
            .ToListAsync(ct);

        var target = neighbours.Where(other => other.ColumnId == column.Id).ToList();
        var place = Math.Min(command.Position ?? target.Count, target.Count);
        target.Insert(place, task);
        PlaceInOrder(target, except: task);

        if (column.Id != task.ColumnId)
        {
            PlaceInOrder(neighbours.Where(other => other.ColumnId == task.ColumnId), except: task);
        }

        task.MoveTo(column, place, currentUser.Id, now, command.PauseReason);

        try
        {
            var saved = await changes.Save(ct);
            return saved.IsFailure ? saved.Error : await changes.Read(task.Id, ct);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // The column was deleted after it was read: only an empty one can be, so it was this one.
            return TaskErrors.ColumnNotInProject;
        }
    }

    private static void PlaceInOrder(IEnumerable<TaskItem> tasks, TaskItem except)
    {
        var position = 0;
        foreach (var other in tasks)
        {
            if (other != except)
            {
                other.ShiftTo(position);
            }

            position++;
        }
    }
}
