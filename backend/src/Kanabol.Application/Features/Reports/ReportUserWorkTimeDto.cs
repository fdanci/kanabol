namespace Kanabol.Application.Features.Reports;

/// <summary>A person's finished tasks, and the estimate and worked time of those that count, a task whole for each assignee.</summary>
public sealed record ReportUserWorkTimeDto(Guid UserId, string Name, int Count, long EstimatedSeconds, long WorkedSeconds);
