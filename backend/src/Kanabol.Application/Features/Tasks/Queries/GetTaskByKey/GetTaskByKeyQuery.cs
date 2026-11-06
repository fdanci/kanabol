using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskByKey;

/// <summary>KAN-123, in any case, as /tasks/KAN-123 opens it (UI spec section 7.5).</summary>
public sealed record GetTaskByKeyQuery(string Key, bool IncludeArchived) : IQuery<TaskItemDto>;
