using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Notifications;
using Kanabol.Application.Features.Notifications.Commands.MarkAllNotificationsRead;
using Kanabol.Application.Features.Notifications.Commands.MarkNotificationRead;
using Kanabol.Application.Features.Notifications.Queries.GetNotifications;
using Kanabol.Application.Features.Notifications.Queries.GetUnreadNotificationCount;
using Kanabol.Domain.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The signed-in user's own notifications (SPEC section 4.4, "Notificările"), for the bell and its page:
/// anyone signed in, without a permission, and nobody reaches another's.
/// </summary>
[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    /// <summary>A page from the newest, 20 by default, only the unread with unread=true, only some types with type, repeated.</summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromServices] IQueryHandler<GetNotificationsQuery, Page<NotificationDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetNotificationsQuery.DefaultPageSize,
        [FromQuery] bool unread = false,
        [FromQuery(Name = "type")] NotificationType[]? types = null)
    {
        var query = new GetNotificationsQuery
        {
            Page = page,
            PageSize = pageSize,
            Unread = unread,
            Types = types ?? [],
        };
        return (await handler.Handle(query, ct)).ToActionResult();
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadNotificationCount(
        [FromServices] IQueryHandler<GetUnreadNotificationCountQuery, NotificationCountDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetUnreadNotificationCountQuery(), ct)).ToActionResult();

    /// <summary>Idempotent: a second time keeps the first time it was read.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(
        Guid id,
        [FromServices] ICommandHandler<MarkNotificationReadCommand, NotificationDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new MarkNotificationReadCommand(id), ct)).ToActionResult();

    /// <summary>How many it marked now.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead(
        [FromServices] ICommandHandler<MarkAllNotificationsReadCommand, NotificationCountDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new MarkAllNotificationsReadCommand(), ct)).ToActionResult();
}
