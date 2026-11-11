using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Projects.Events;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;

namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// Tells a project's board what changed in its tasks, once the change is committed (SPEC section 9). The
/// payloads are small: the board moves a card itself on TaskMoved and reads itself again on the rest. Each
/// carries the X-Connection-Id of the request, so the tab that made the change can ignore it. A change of
/// assignees raises no TaskUpdatedEvent, so TaskAssignedEvent sends TaskUpdated with Assignees, also when a
/// creation assigns people. A comment sends CommentAdded with its id, and a file attached or taken off
/// TaskUpdated with Attachments, for the count on the card. A change of the columns sends ColumnsChanged
/// (group 23), and the boards read themselves again. The domain knows nothing of SignalR:
/// IBoardNotifier does.
/// </summary>
public sealed class BoardEventsHandler(IBoardNotifier notifier, IRequestContext request) :
    IDomainEventHandler<TaskCreatedEvent>,
    IDomainEventHandler<TaskMovedEvent>,
    IDomainEventHandler<TaskUpdatedEvent>,
    IDomainEventHandler<TaskAssignedEvent>,
    IDomainEventHandler<TaskArchivedEvent>,
    IDomainEventHandler<TaskRestoredEvent>,
    IDomainEventHandler<CommentAddedEvent>,
    IDomainEventHandler<AttachmentsChangedEvent>,
    IDomainEventHandler<ColumnsChangedEvent>
{
    public Task Handle(TaskCreatedEvent domainEvent, CancellationToken ct) =>
        Notify(domainEvent.ProjectId, BoardEvents.TaskCreated, new TaskEventDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.CreatedById, request.ConnectionId), ct);

    public Task Handle(TaskMovedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.TaskMoved,
            new TaskMovedDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.MovedById, request.ConnectionId, domainEvent.ToColumnId, domainEvent.Position),
            ct);

    public Task Handle(TaskUpdatedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.TaskUpdated,
            new TaskUpdatedDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.ChangedById, request.ConnectionId, domainEvent.Field),
            ct);

    public Task Handle(TaskAssignedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.TaskUpdated,
            new TaskUpdatedDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.AssignedById, request.ConnectionId, TaskHistoryFields.Assignees),
            ct);

    public Task Handle(TaskArchivedEvent domainEvent, CancellationToken ct) =>
        Notify(domainEvent.ProjectId, BoardEvents.TaskArchived, new TaskEventDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.ArchivedById, request.ConnectionId), ct);

    public Task Handle(TaskRestoredEvent domainEvent, CancellationToken ct) =>
        Notify(domainEvent.ProjectId, BoardEvents.TaskRestored, new TaskEventDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.RestoredById, request.ConnectionId), ct);

    public Task Handle(CommentAddedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.CommentAdded,
            new CommentAddedDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.AuthorId, request.ConnectionId, domainEvent.CommentId),
            ct);

    public Task Handle(AttachmentsChangedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.TaskUpdated,
            new TaskUpdatedDto(domainEvent.TaskId, domainEvent.ProjectId, domainEvent.ChangedById, request.ConnectionId, nameof(TaskItem.Attachments)),
            ct);

    public Task Handle(ColumnsChangedEvent domainEvent, CancellationToken ct) =>
        Notify(
            domainEvent.ProjectId,
            BoardEvents.ColumnsChanged,
            new ColumnsChangedDto(domainEvent.ProjectId, domainEvent.ChangedById, request.ConnectionId),
            ct);

    private Task Notify(Guid projectId, string eventName, object payload, CancellationToken ct) =>
        notifier.NotifyProjectAsync(projectId, eventName, payload, ct);
}
