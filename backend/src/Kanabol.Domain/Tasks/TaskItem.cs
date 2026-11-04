using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

public sealed class TaskItem : AuditableEntity, IArchivable
{
    private readonly List<Comment> _comments = [];
    private readonly List<Attachment> _attachments = [];
    private readonly List<TaskHistoryEntry> _history = [];

    public TaskItem(
        Guid projectId,
        Guid columnId,
        string title,
        string? description,
        TaskPriority priority,
        int position,
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null)
    {
        ProjectId = projectId;
        ColumnId = columnId;
        Title = title;
        Description = description;
        Priority = priority;
        Position = position;
        AssigneeId = assigneeId;
        DueDate = dueDate;
    }

    private TaskItem()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid ColumnId { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public TaskPriority Priority { get; private set; }

    public DateTimeOffset? DueDate { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedById { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments;

    public IReadOnlyCollection<Attachment> Attachments => _attachments;

    public IReadOnlyCollection<TaskHistoryEntry> History => _history;
}
