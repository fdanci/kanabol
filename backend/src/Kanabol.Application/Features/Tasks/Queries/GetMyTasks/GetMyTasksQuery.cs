using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetMyTasks;

/// <summary>
/// The caller's own tasks across every project (SPEC section 4.4, "Panoul principal și task-urile mele"):
/// the project list's filters but the assignee, which all have to hold, plus only the open ones with Open.
/// By deadline from the soonest unless asked otherwise, those without one last, then by key from the newest.
/// </summary>
public sealed record GetMyTasksQuery : IQuery<Page<MyTaskDto>>
{
    public const string DefaultSort = "dueDate:asc";

    public const int MaxSearchLength = GetProjectTasksQuery.MaxSearchLength;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public bool IncludeArchived { get; init; }

    public string Sort { get; init; } = DefaultSort;

    /// <summary>Only those not completed: completed_at null.</summary>
    public bool Open { get; init; }

    public IReadOnlyList<TaskPriority> Priorities { get; init; } = [];

    public bool Overdue { get; init; }

    public string? Search { get; init; }
}
