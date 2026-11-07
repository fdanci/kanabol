using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Commands.AssignTask;

/// <summary>
/// Whoever comes onto the task has to be an active member of its project, or the whole change is a 400 on
/// assigneeIds. Whoever is already on it stays even if deactivated since, until someone takes them off
/// (SPEC section 4.4).
/// </summary>
public sealed class AssignTaskCommandHandler(IAppDbContext db, TaskChanges changes) : ICommandHandler<AssignTaskCommand, TaskItemDto>
{
    public Task<Result<TaskItemDto>> Handle(AssignTaskCommand command, CancellationToken ct) =>
        changes.Apply(
            command.TaskId,
            (task, by, now) => task.Assign(command.AssigneeIds, by, now),
            ct,
            task => CanBeAssigned(task, command.AssigneeIds, ct));

    private async Task<Result> CanBeAssigned(TaskItem task, IReadOnlyList<Guid> assigneeIds, CancellationToken ct)
    {
        Guid[] coming = [.. assigneeIds.Where(userId => task.Assignees.All(assignee => assignee.UserId != userId))];
        return await db.AreAllActiveMembersOf(task.ProjectId, coming, ct) ? Result.Success() : TaskErrors.AssigneeNotAMember;
    }
}
