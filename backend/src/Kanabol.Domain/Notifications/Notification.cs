using Kanabol.Domain.Common;

namespace Kanabol.Domain.Notifications;

public sealed class Notification : Entity
{
    public Notification(Guid userId, NotificationType type, Guid? taskId, string message, DateTimeOffset createdAt)
    {
        UserId = userId;
        Type = type;
        TaskId = taskId;
        Message = message;
        CreatedAt = createdAt;
    }

    private Notification()
    {
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public Guid? TaskId { get; private set; }

    public string Message { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }
}
