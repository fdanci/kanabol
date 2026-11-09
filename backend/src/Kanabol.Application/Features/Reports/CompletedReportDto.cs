namespace Kanabol.Application.Features.Reports;

/// <summary>
/// The tasks finished in the interval (SPEC section 5.1.1): Total counts each once, as ByProject does, and
/// ByUser counts a task for each of its assignees, so its sum may go past Total; a task without an assignee
/// is in Total and ByProject only. Series has a period for each step from the interval's start.
/// </summary>
public sealed record CompletedReportDto(
    int Total,
    IReadOnlyList<ReportUserCountDto> ByUser,
    IReadOnlyList<ReportProjectCountDto> ByProject,
    IReadOnlyList<ReportPeriodCountDto> Series);
