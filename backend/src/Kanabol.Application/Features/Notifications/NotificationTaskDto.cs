namespace Kanabol.Application.Features.Notifications;

/// <summary>The task of a notification, with its key and its title of now, to open it by: /tasks/KAN-1.</summary>
public sealed record NotificationTaskDto(Guid Id, string Key, string Title);
