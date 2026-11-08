using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Reports.Queries.GetDeadlinesReport;

/// <summary>The deadlines kept and missed by the tasks finished in [From, To), and those overdue now (SPEC section 5.1.1).</summary>
public sealed record GetDeadlinesReportQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ProjectId,
    Guid? UserId,
    ReportGranularity Granularity) : IQuery<DeadlinesReportDto>, IReportFilter;
