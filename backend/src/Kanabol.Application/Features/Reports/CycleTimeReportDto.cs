namespace Kanabol.Application.Features.Reports;

/// <summary>
/// How long the finished tasks took (SPEC section 5.1.1), over the tasks the completed report counts:
/// LeadTime from creation to completion, CycleTime from the first exit from the column at position 0, and
/// with a project only, the time in each of its columns, in the board's order. Null without a project.
/// </summary>
public sealed record CycleTimeReportDto(
    int Count,
    ReportDurationDto LeadTime,
    ReportDurationDto CycleTime,
    IReadOnlyList<ReportColumnDurationDto>? PerColumn);
