using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Commands.RenameTask;

public sealed class RenameTaskCommandHandler(TaskChanges changes) : ICommandHandler<RenameTaskCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(RenameTaskCommand command, CancellationToken ct) =>
        changes.Apply(command.TaskId, (task, by, now) => task.Rename(command.Title, by, now), ct);
}
