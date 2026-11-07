namespace Kanabol.Domain.Tasks;

/// <summary>
/// Someone a task is assigned to, one of any number (SPEC section 5.1). AssignedById is null only for
/// the assignees the migration moved from task_items.assignee_id, since nobody knows who made those.
/// </summary>
public sealed class TaskAssignee
{
    internal TaskAssignee(Guid taskId, Guid userId, Guid assignedById, DateTimeOffset assignedAt)
    {
        TaskId = taskId;
        UserId = userId;
        AssignedById = assignedById;
        AssignedAt = assignedAt;
    }

    private TaskAssignee()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public Guid? AssignedById { get; private set; }
}
