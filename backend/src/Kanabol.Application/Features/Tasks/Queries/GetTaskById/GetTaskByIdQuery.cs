using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskById;

public sealed record GetTaskByIdQuery(Guid Id, bool IncludeArchived) : IQuery<TaskItemDto>;
