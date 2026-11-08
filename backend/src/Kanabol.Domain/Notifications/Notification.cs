using Kanabol.Domain.Common;

namespace Kanabol.Domain.Notifications;

/// <summary>
/// In-app only (SPEC section 5.1). Each one announces itself with NotificationCreatedEvent, whoever made it:
/// a domain event handler or the overdue tasks worker (SPEC section 4.3).
/// </summary>
public sealed class Notification : Entity
{
    public const int MaxMessageLength = 500;

    public Notification(Guid userId, NotificationType type, Guid? taskId, string message, DateTimeOffset createdAt)
    {
        UserId = userId;
        Type = type;
        TaskId = taskId;
        Message = message.Length <= MaxMessageLength ? message : string.Concat(message.AsSpan(0, MaxMessageLength - 1), "…");
        CreatedAt = createdAt;
        Raise(new NotificationCreatedEvent(Id, userId));
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

    /// <summary>Reads it now, unless it was read before, whose first time stays (SPEC section 4.4). True when it changed.</summary>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = now;
        return true;
    }
}
