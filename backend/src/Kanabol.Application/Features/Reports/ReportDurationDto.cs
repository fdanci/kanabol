namespace Kanabol.Application.Features.Reports;

/// <summary>An average and a median in whole seconds, both null when there is nothing to measure.</summary>
public sealed record ReportDurationDto(long? AverageSeconds, long? MedianSeconds);
