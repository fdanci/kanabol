using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;

/// <summary>How long the tasks finished in [From, To) took, in a project and for a person when given (SPEC section 5.1.1).</summary>
public sealed record GetCycleTimeReportQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? ProjectId, Guid? UserId)
    : IQuery<CycleTimeReportDto>, IReportFilter;
