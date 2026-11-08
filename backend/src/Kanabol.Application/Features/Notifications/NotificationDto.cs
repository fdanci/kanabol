using System.Text.Json.Serialization;
using Kanabol.Domain.Notifications;

namespace Kanabol.Application.Features.Notifications;

/// <summary>
/// A notification as its user sees it (SPEC section 4.4, "Notificările"). Message is the API's English text;
/// the UI writes its own from Type and Task (UI spec section 7.6). Task is the task as it is now, null for a
/// notification without one, and stays when its user can no longer open the task.
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NotificationType>))] NotificationType Type,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    NotificationTaskDto? Task);
