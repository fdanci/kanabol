namespace Kanabol.Application.Features.Reports;

/// <summary>A period of the deadlines' series: the tasks with a deadline finished in it, by it and after it.</summary>
public sealed record ReportDeadlinePeriodDto(DateTimeOffset PeriodStart, int OnTime, int Late);
