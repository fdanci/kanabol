using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Dashboard;
using Kanabol.Application.Features.Dashboard.Queries.GetMySummary;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Queries.GetMyTasks;
using Kanabol.Domain.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The signed-in user's own numbers and tasks, for the main panel and "Task-urile mele" (SPEC section 4.4,
/// "Panoul principal și task-urile mele"): anyone signed in, without a permission, about their own only.
/// </summary>
[ApiController]
[Route("api/me")]
public sealed class MeController : ControllerBase
{
    /// <summary>The four numbers of the main panel. weekStart, required and at most now, is the Monday at midnight in the caller's time.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetMySummary(
        [FromServices] IQueryHandler<GetMySummaryQuery, MySummaryDto> handler,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? weekStart = null) =>
        (await handler.Handle(new GetMySummaryQuery(weekStart), ct)).ToActionResult();

    /// <summary>A page of the caller's own tasks, by deadline from the soonest unless sort says otherwise; only the open ones with open=true.</summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> GetMyTasks(
        [FromServices] IQueryHandler<GetMyTasksQuery, Page<MyTaskDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string sort = GetMyTasksQuery.DefaultSort,
        [FromQuery] bool open = false,
        [FromQuery(Name = "priority")] TaskPriority[]? priorities = null,
        [FromQuery] bool overdue = false,
        [FromQuery] string? search = null)
    {
        var query = new GetMyTasksQuery
        {
            Page = page,
            PageSize = pageSize,
            IncludeArchived = includeArchived,
            Sort = sort,
            Open = open,
            Priorities = priorities ?? [],
            Overdue = overdue,
            Search = search,
        };
        return (await handler.Handle(query, ct)).ToActionResult();
    }
}
