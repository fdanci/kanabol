namespace Kanabol.Application.Features.Reports;

public sealed record ReportProjectCountDto(Guid ProjectId, string Key, string Name, int Count);
