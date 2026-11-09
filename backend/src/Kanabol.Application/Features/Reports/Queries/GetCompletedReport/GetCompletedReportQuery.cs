using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Reports.Queries.GetCompletedReport;

/// <summary>The tasks finished in [From, To), in a project and for a person when given, by week or by month (SPEC section 5.1.1).</summary>
public sealed record GetCompletedReportQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ProjectId,
    Guid? UserId,
    ReportGranularity Granularity) : IQuery<CompletedReportDto>, IReportFilter;
