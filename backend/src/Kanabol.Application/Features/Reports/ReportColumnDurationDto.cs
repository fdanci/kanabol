namespace Kanabol.Application.Features.Reports;

/// <summary>The time the finished tasks spent in a column, null when none of them was in it.</summary>
public sealed record ReportColumnDurationDto(Guid ColumnId, string Name, long? AverageSeconds, long? MedianSeconds);
