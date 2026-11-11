using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskEstimate;

/// <summary>
/// The estimate changes as any property of the task does (SPEC section 4.4), with its history entry and 409 on
/// the task's version. Taking it away is refused once the task entered În lucru: a move into it in the
/// history, or the task there now, as the migration left some without one.
/// </summary>
public sealed class SetTaskEstimateCommandHandler(IAppDbContext db, TaskChanges changes) : ICommandHandler<SetTaskEstimateCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(SetTaskEstimateCommand command, CancellationToken ct) =>
        changes.Apply(
            command.TaskId,
            (task, by, now) => task.SetEstimate(command.EstimatedMinutes, by, now),
            ct,
            async task => command.EstimatedMinutes is null && await EnteredInProgress(task, ct) ? TaskErrors.EstimateKept : Result.Success());

    private async Task<bool> EnteredInProgress(TaskItem task, CancellationToken ct)
    {
        var inProgress = await db.BoardColumns
            .Where(column => column.ProjectId == task.ProjectId && column.Kind == ColumnKind.InProgress)
            .Select(column => column.Id)
            .SingleAsync(ct);
        var entry = inProgress.ToString();
        return task.ColumnId == inProgress
            || await db.TaskHistoryEntries.AnyAsync(found => found.TaskId == task.Id && found.Field == TaskHistoryFields.ColumnId && found.NewValue == entry, ct);
    }
}
