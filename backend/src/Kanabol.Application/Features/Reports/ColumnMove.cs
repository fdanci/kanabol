namespace Kanabol.Application.Features.Reports;

/// <summary>A ColumnId entry of a task's history: the column it left and the one it entered, as ids in text.</summary>
public sealed record ColumnMove(string? From, string? To, DateTimeOffset At);
