using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects.Events;

/// <summary>A column of the project was added, renamed, moved or deleted (SPEC section 9, group 23).</summary>
public sealed record ColumnsChangedEvent(Guid ProjectId, Guid ChangedById) : IDomainEvent;
