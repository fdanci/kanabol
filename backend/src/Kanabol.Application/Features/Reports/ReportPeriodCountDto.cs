namespace Kanabol.Application.Features.Reports;

/// <summary>A period of a series, from PeriodStart, in the offset the interval's start came with.</summary>
public sealed record ReportPeriodCountDto(DateTimeOffset PeriodStart, int Count);
