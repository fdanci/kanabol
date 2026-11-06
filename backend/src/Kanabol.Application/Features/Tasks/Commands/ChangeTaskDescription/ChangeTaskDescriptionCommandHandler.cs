using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;

public sealed class ChangeTaskDescriptionCommandHandler(TaskChanges changes) : ICommandHandler<ChangeTaskDescriptionCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(ChangeTaskDescriptionCommand command, CancellationToken ct) =>
        changes.Apply(command.TaskId, (task, by, now) => task.ChangeDescription(command.Description, by, now), ct);
}
