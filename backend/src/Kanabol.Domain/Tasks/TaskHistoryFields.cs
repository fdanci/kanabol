namespace Kanabol.Domain.Tasks;

/// <summary>
/// What a history entry says changed: the name of the property, so the reports find the moves between
/// columns under ColumnId (SPEC section 5.1.1).
/// </summary>
public static class TaskHistoryFields
{
    public const string Title = nameof(TaskItem.Title);
    public const string Description = nameof(TaskItem.Description);
    public const string AssigneeId = nameof(TaskItem.AssigneeId);
    public const string Priority = nameof(TaskItem.Priority);
    public const string DueDate = nameof(TaskItem.DueDate);
    public const string ColumnId = nameof(TaskItem.ColumnId);
    public const string ArchivedAt = nameof(TaskItem.ArchivedAt);
}
