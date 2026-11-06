using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;

public sealed class SetTaskPriorityCommandHandler(TaskChanges changes) : ICommandHandler<SetTaskPriorityCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(SetTaskPriorityCommand command, CancellationToken ct) =>
        changes.Apply(command.TaskId, (task, by, now) => task.SetPriority(command.Priority, by, now), ct);
}
