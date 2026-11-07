using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;

/// <summary>
/// The board's filters (UI spec section 7.4), which all have to hold: one person among the assignees,
/// any of the priorities given, only the overdue ones, and a part of the title in any case.
/// </summary>
public sealed record GetProjectTasksQuery(Guid ProjectId) : IQuery<Page<TaskSummaryDto>>
{
    public const int MaxSearchLength = TaskItem.MaxTitleLength;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public bool IncludeArchived { get; init; }

    public string Sort { get; init; } = TaskSort.Default;

    public Guid? AssigneeId { get; init; }

    public IReadOnlyList<TaskPriority> Priorities { get; init; } = [];

    public bool Overdue { get; init; }

    public string? Search { get; init; }
}
