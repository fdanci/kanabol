namespace Kanabol.Application.Features.Dashboard;

/// <summary>
/// The main panel's four tiles (UI spec section 7.2): the caller's open tasks, the overdue ones among them,
/// those finished since the week's start, and the open projects they are a member of. No deltas in v1.
/// </summary>
public sealed record MySummaryDto(int OpenTasks, int OverdueTasks, int CompletedThisWeek, int ActiveProjects);
