using Kanabol.Domain.Notifications;

namespace Kanabol.Domain.Tests.Notifications;

/// <summary>SPEC section 4.3: every notification announces itself, which group 14 sends on to the user's SignalR group.</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Always_RaisesNotificationCreated()
    {
        var userId = Guid.CreateVersion7();

        var notification = new Notification(userId, NotificationType.TaskAssigned, Guid.CreateVersion7(), "Ana assigned you KAN-1: Login", _now);

        notification.DomainEvents.Should().ContainSingle().Which.Should().Be(new NotificationCreatedEvent(notification.Id, userId));
        notification.ReadAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_WhenTheMessageIsOverTheLimit_CutsItWithAnEllipsis()
    {
        var notification = new Notification(Guid.CreateVersion7(), NotificationType.TaskAssigned, null, new string('a', 600), _now);

        notification.Message.Should().HaveLength(Notification.MaxMessageLength).And.EndWith("…");
    }
}
