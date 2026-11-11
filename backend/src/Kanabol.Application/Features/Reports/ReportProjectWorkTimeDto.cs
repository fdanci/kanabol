namespace Kanabol.Application.Features.Reports;

/// <summary>A project's finished tasks, and the estimate and worked time of those that count.</summary>
public sealed record ReportProjectWorkTimeDto(Guid ProjectId, string Key, string Name, int Count, long EstimatedSeconds, long WorkedSeconds);
