using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

/// <summary>A page of the task's history, from the newest (SPEC section 4.4, "Istoricul unui task").</summary>
public sealed record GetTaskHistoryQuery(Guid TaskId) : IQuery<Page<TaskHistoryEntryDto>>
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
