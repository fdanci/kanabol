namespace Kanabol.Application.Features.Reports;

/// <summary>
/// How the deadlines were kept (SPEC section 5.1.1). Overdue is today's state, whatever the interval: open
/// tasks, not archived, past their deadline, in projects not archived. Of the tasks finished in the interval
/// with a deadline, CompletedOnTime by it and CompletedLate after it; OnTimeRate is the share on time, null
/// when none had a deadline. Series has a period for each step from the interval's start.
/// </summary>
public sealed record DeadlinesReportDto(
    int Overdue,
    int CompletedLate,
    int CompletedOnTime,
    double? OnTimeRate,
    IReadOnlyList<ReportDeadlinePeriodDto> Series);
