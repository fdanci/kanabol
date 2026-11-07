namespace Kanabol.Application.Features.Realtime;

/// <summary>NotificationCreated, on the user's own group: the bell reads its notifications again (SPEC section 9).</summary>
public sealed record NotificationCreatedDto(Guid NotificationId);
