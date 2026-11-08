using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Dashboard.Queries.GetMySummary;

/// <summary>
/// WeekStart is the Monday at midnight in the caller's own time, which the browser sends as an instant:
/// required, and at most now (SPEC section 4.4, "Panoul principal și task-urile mele").
/// </summary>
public sealed record GetMySummaryQuery(DateTimeOffset? WeekStart) : IQuery<MySummaryDto>;
