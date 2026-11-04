using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

public sealed class TaskHistoryEntry : Entity
{
    public TaskHistoryEntry(Guid taskId, string field, string? oldValue, string? newValue, Guid changedById, DateTimeOffset changedAt)
    {
        TaskId = taskId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedById = changedById;
        ChangedAt = changedAt;
    }

    private TaskHistoryEntry()
    {
    }

    public Guid TaskId { get; private set; }

    public string Field { get; private set; } = null!;

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public Guid ChangedById { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}
