using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Queries.GetArchivedTasks;

/// <summary>A page of the project's archived cards, the board's column "Arhivate" (SPEC section 4.4).</summary>
public sealed record GetArchivedTasksQuery(Guid ProjectId, int Page = 1, int PageSize = Paging.DefaultPageSize) : IQuery<Page<ArchivedTaskDto>>;
