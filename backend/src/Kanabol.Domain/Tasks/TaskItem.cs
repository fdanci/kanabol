using System.Globalization;
using Kanabol.Domain.Common;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks.Events;

namespace Kanabol.Domain.Tasks;

/// <summary>
/// A task of a project. Every change goes through a method that writes its own history entry and
/// raises a domain event (SPEC section 5.1), so the history never depends on the handler. A change
/// to the value the task already has writes nothing.
/// </summary>
public sealed class TaskItem : AuditableEntity, IArchivable
{
    public const int MaxTitleLength = 300;

    /// <summary>Markdown for a task, not a document: about ten pages of text.</summary>
    public const int MaxDescriptionLength = 20_000;

    private readonly List<Comment> _comments = [];
    private readonly List<Attachment> _attachments = [];
    private readonly List<TaskHistoryEntry> _history = [];

    private TaskItem(Project project, BoardColumn column, string title, string? description, TaskPriority priority, int position)
    {
        ProjectId = project.Id;
        ColumnId = column.Id;
        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        Priority = priority;
        Position = position;
    }

    private TaskItem()
    {
    }

    public Guid ProjectId { get; private set; }

    /// <summary>Unique in the project, from its counter, and kept after archiving: KAN-123 is TaskKey.Format("KAN", 123).</summary>
    public int Number { get; private set; }

    public Guid ColumnId { get; private set; }

    public string Title { get; private set; } = null!;

    /// <summary>Basic markdown, as written. The client renders it through a sanitizer (SPEC section 16).</summary>
    public string? Description { get; private set; }

    public TaskPriority Priority { get; private set; }

    public DateTimeOffset? DueDate { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public int Position { get; private set; }

    /// <summary>When the task last entered a done column, and null while it's outside one.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedById { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments;

    public IReadOnlyCollection<Attachment> Attachments => _attachments;

    public IReadOnlyCollection<TaskHistoryEntry> History => _history;

    /// <summary>
    /// A new task in an open project, with the project's next number. One created straight into a done
    /// column is completed at once, and one created for someone is assigned to them, which they hear of
    /// like any assignment (SPEC section 4.3).
    /// </summary>
    public static TaskItem Create(
        Project project,
        BoardColumn column,
        string title,
        string? description,
        TaskPriority priority,
        int position,
        Guid? assigneeId,
        DateTimeOffset? dueDate,
        Guid createdById,
        DateTimeOffset now)
    {
        if (project.ArchivedAt is not null)
        {
            throw new InvalidOperationException("An archived project takes no new tasks.");
        }

        if (column.ProjectId != project.Id)
        {
            throw new ArgumentException("A task starts in a column of its own project.", nameof(column));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(position);

        var task = new TaskItem(project, column, title, description, priority, position)
        {
            DueDate = dueDate?.ToUniversalTime(),
            AssigneeId = assigneeId,
            CompletedAt = column.IsDone ? now : null,
        };
        task.Number = project.TakeTaskNumber();
        task.Raise(new TaskCreatedEvent(task.Id, project.Id, task.Number, createdById));
        if (assigneeId is not null)
        {
            task.Raise(new TaskAssignedEvent(task.Id, project.Id, assigneeId, null, createdById));
        }

        return task;
    }

    public void Rename(string title, Guid changedById, DateTimeOffset now)
    {
        var normalized = NormalizeTitle(title);
        if (normalized == Title)
        {
            return;
        }

        Record(TaskHistoryFields.Title, Title, normalized, changedById, now);
        Title = normalized;
        Raise(new TaskUpdatedEvent(Id, ProjectId, TaskHistoryFields.Title, changedById));
    }

    public void ChangeDescription(string? description, Guid changedById, DateTimeOffset now)
    {
        var normalized = NormalizeDescription(description);
        if (normalized == Description)
        {
            return;
        }

        Record(TaskHistoryFields.Description, Description, normalized, changedById, now);
        Description = normalized;
        Raise(new TaskUpdatedEvent(Id, ProjectId, TaskHistoryFields.Description, changedById));
    }

    /// <summary>Null leaves the task to nobody. Whether the assignee is a member is the handler's to check first.</summary>
    public void Assign(Guid? assigneeId, Guid changedById, DateTimeOffset now)
    {
        if (assigneeId == AssigneeId)
        {
            return;
        }

        var previous = AssigneeId;
        Record(TaskHistoryFields.AssigneeId, previous?.ToString(), assigneeId?.ToString(), changedById, now);
        AssigneeId = assigneeId;
        Raise(new TaskAssignedEvent(Id, ProjectId, assigneeId, previous, changedById));
    }

    public void SetPriority(TaskPriority priority, Guid changedById, DateTimeOffset now)
    {
        if (priority == Priority)
        {
            return;
        }

        Record(TaskHistoryFields.Priority, Priority.ToString(), priority.ToString(), changedById, now);
        Priority = priority;
        Raise(new TaskUpdatedEvent(Id, ProjectId, TaskHistoryFields.Priority, changedById));
    }

    /// <summary>The same moment in another offset is the same deadline: DateTimeOffset compares instants. Stored in UTC.</summary>
    public void SetDueDate(DateTimeOffset? dueDate, Guid changedById, DateTimeOffset now)
    {
        if (dueDate == DueDate)
        {
            return;
        }

        var utc = dueDate?.ToUniversalTime();
        Record(TaskHistoryFields.DueDate, Format(DueDate), Format(utc), changedById, now);
        DueDate = utc;
        Raise(new TaskUpdatedEvent(Id, ProjectId, TaskHistoryFields.DueDate, changedById));
    }

    /// <summary>
    /// To a column of the same project, at a place in it. Entering a done column completes the task,
    /// leaving it clears that. Only a change of column goes in the history: a reorder in the same
    /// column isn't a change of state, and the reports read the columns (SPEC section 5.1.1).
    /// </summary>
    public void MoveTo(BoardColumn column, int position, Guid changedById, DateTimeOffset now)
    {
        if (column.ProjectId != ProjectId)
        {
            throw new ArgumentException("A task moves only between the columns of its own project.", nameof(column));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(position);

        if (column.Id == ColumnId && position == Position)
        {
            return;
        }

        var from = ColumnId;
        if (column.Id != from)
        {
            Record(TaskHistoryFields.ColumnId, from.ToString(), column.Id.ToString(), changedById, now);
            ColumnId = column.Id;
        }

        Position = position;
        CompletedAt = column.IsDone ? CompletedAt ?? now : null;
        Raise(new TaskMovedEvent(Id, ProjectId, from, column.Id, position, changedById));
    }

    /// <summary>
    /// Another place in the same column, to keep the column's order without gaps around a move. It isn't
    /// a change of this task, so it writes no history and raises nothing: the moved task's event says it all.
    /// </summary>
    public void ShiftTo(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Position = position;
    }

    /// <summary>Archiving twice keeps the first time, and who did it.</summary>
    public void Archive(Guid archivedById, DateTimeOffset now)
    {
        if (ArchivedAt is not null)
        {
            return;
        }

        Record(TaskHistoryFields.ArchivedAt, null, Format(now), archivedById, now);
        ArchivedAt = now;
        ArchivedById = archivedById;
        Raise(new TaskArchivedEvent(Id, ProjectId, archivedById));
    }

    public void Restore(Guid restoredById, DateTimeOffset now)
    {
        if (ArchivedAt is null)
        {
            return;
        }

        Record(TaskHistoryFields.ArchivedAt, Format(ArchivedAt), null, restoredById, now);
        ArchivedAt = null;
        ArchivedById = null;
        Raise(new TaskRestoredEvent(Id, ProjectId, restoredById));
    }

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return title.Trim();
    }

    // Leading spaces mean something in markdown, such as a code block, so only the blank end goes.
    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.TrimEnd();

    private static string? Format(DateTimeOffset? moment) => moment?.ToString("O", CultureInfo.InvariantCulture);

    private void Record(string field, string? oldValue, string? newValue, Guid changedById, DateTimeOffset now) =>
        _history.Add(new TaskHistoryEntry(Id, field, oldValue, newValue, changedById, now));
}
