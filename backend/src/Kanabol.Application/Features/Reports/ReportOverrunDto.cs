namespace Kanabol.Application.Features.Reports;

/// <summary>A finished task worked past its estimate.</summary>
public sealed record ReportOverrunDto(Guid TaskId, string Key, string Title, long EstimatedSeconds, long WorkedSeconds);
