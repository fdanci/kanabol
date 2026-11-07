namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// The names of what the board hub sends (SPEC section 9). CommentAdded comes with group 15, when
/// CommentAddedEvent exists.
/// </summary>
public static class BoardEvents
{
    public const string TaskCreated = nameof(TaskCreated);
    public const string TaskMoved = nameof(TaskMoved);
    public const string TaskUpdated = nameof(TaskUpdated);
    public const string TaskArchived = nameof(TaskArchived);
    public const string TaskRestored = nameof(TaskRestored);
    public const string NotificationCreated = nameof(NotificationCreated);
}
