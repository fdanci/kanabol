using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Reports.Queries.GetWorkTimeReport;

/// <summary>The estimate against the worked time of the tasks finished in [From, To), in a project and for a person when given (SPEC section 5.1.1).</summary>
public sealed record GetWorkTimeReportQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? ProjectId, Guid? UserId)
    : IQuery<WorkTimeReportDto>, IReportFilter;
