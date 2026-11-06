using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;

public sealed class SetTaskDueDateCommandHandler(TaskChanges changes) : ICommandHandler<SetTaskDueDateCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(SetTaskDueDateCommand command, CancellationToken ct) =>
        changes.Apply(command.TaskId, (task, by, now) => task.SetDueDate(command.DueDate, by, now), ct);
}
