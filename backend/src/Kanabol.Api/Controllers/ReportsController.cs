using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetCompletedReport;
using Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The reports (SPEC section 5.1.1, "Endpoint-urile rapoartelor"), each with the same parameters: from and
/// to, instants, required, from before to and at most 366 days apart, and projectId and userId, optional.
/// Who may read them is checked in the handlers, not here: reports.view, or an owner of the projectId given.
/// </summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>The tasks finished in [from, to): the total, by user, by project and by week or month from from.</summary>
    [HttpGet("completed")]
    public async Task<IActionResult> GetCompletedReport(
        [FromServices] IQueryHandler<GetCompletedReportQuery, CompletedReportDto> handler,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] ReportGranularity granularity = ReportGranularity.Week) =>
        (await handler.Handle(new GetCompletedReportQuery(from, to, projectId, userId, granularity), ct)).ToActionResult();

    /// <summary>Lead and cycle time of the tasks finished in [from, to), averages and medians in seconds, and with projectId the time in each column.</summary>
    [HttpGet("cycle-time")]
    public async Task<IActionResult> GetCycleTimeReport(
        [FromServices] IQueryHandler<GetCycleTimeReportQuery, CycleTimeReportDto> handler,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? userId = null) =>
        (await handler.Handle(new GetCycleTimeReportQuery(from, to, projectId, userId), ct)).ToActionResult();
}
