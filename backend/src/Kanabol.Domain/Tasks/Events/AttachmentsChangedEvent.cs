using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>
/// A file was attached to the task, or taken off it: the board hears TaskUpdated with Attachments, for the
/// count on the card (SPEC section 9). Attachments aren't in the task's history.
/// </summary>
public sealed record AttachmentsChangedEvent(Guid TaskId, Guid ProjectId, Guid ChangedById) : IDomainEvent;
