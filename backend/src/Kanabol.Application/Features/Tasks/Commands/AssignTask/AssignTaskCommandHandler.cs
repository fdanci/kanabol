using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Commands.AssignTask;

/// <summary>Only to an active member of the task's project: anyone else is a 400 on assigneeId.</summary>
public sealed class AssignTaskCommandHandler(IAppDbContext db, TaskChanges changes) : ICommandHandler<AssignTaskCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(AssignTaskCommand command, CancellationToken ct) =>
        changes.Apply(
            command.TaskId,
            (task, by, now) => task.Assign(command.AssigneeId, by, now),
            ct,
            task => CanBeAssigned(task, command.AssigneeId, ct));

    private async Task<Result> CanBeAssigned(TaskItem task, Guid? assigneeId, CancellationToken ct) =>
        assigneeId is not { } userId || userId == task.AssigneeId || await db.IsActiveMemberOf(task.ProjectId, userId, ct)
            ? Result.Success()
            : TaskErrors.AssigneeNotAMember;
}
