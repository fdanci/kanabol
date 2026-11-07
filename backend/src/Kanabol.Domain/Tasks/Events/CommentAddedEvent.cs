using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>
/// Someone commented on a task: its assignees and its creator hear of it, but the author (SPEC section 4.3),
/// and its board reads itself again for the count (SPEC section 9). Editing a comment raises nothing.
/// </summary>
public sealed record CommentAddedEvent(Guid CommentId, Guid TaskId, Guid ProjectId, Guid AuthorId) : IDomainEvent;
