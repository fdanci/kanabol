namespace Kanabol.Application.Features.Realtime;

/// <summary>The names of what the board hub sends (SPEC section 9).</summary>
public static class BoardEvents
{
    public const string TaskCreated = nameof(TaskCreated);
    public const string TaskMoved = nameof(TaskMoved);
    public const string TaskUpdated = nameof(TaskUpdated);
    public const string TaskArchived = nameof(TaskArchived);
    public const string TaskRestored = nameof(TaskRestored);
    public const string CommentAdded = nameof(CommentAdded);
    public const string ColumnsChanged = nameof(ColumnsChanged);
    public const string NotificationCreated = nameof(NotificationCreated);
}
