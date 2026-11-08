namespace Kanabol.Application.Features.Reports;

public sealed record ReportUserCountDto(Guid UserId, string Name, int Count);
