using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Notifications;

public static class NotificationErrors
{
    /// <summary>One that doesn't exist, or someone else's: nobody learns that another's exists (SPEC section 4.4).</summary>
    public static readonly Error NotFound = Error.NotFound("Notification.NotFound", "There is no such notification of yours.");
}
