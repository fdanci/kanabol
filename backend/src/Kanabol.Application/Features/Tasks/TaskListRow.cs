using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

/// <summary>A row of a task list as the query reads it, with what its sorts need beside what the DTOs show.</summary>
internal sealed class TaskListRow
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string ProjectKey { get; init; } = "";

    public string ProjectName { get; init; } = "";

    public int Number { get; init; }

    public string Title { get; init; } = "";

    public Guid ColumnId { get; init; }

    public string ColumnName { get; init; } = "";

    public int ColumnPosition { get; init; }

    public ColumnKind ColumnKind { get; init; }

    public TaskPriority Priority { get; init; }

    public int PriorityRank { get; init; }

    public DateTimeOffset? DueDate { get; init; }

    public List<TaskPersonDto> Assignees { get; init; } = [];

    public string? FirstAssigneeName { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? ArchivedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public int? EstimatedMinutes { get; init; }

    public int WorkedSeconds { get; init; }

    public DateTimeOffset? WorkStartedAt { get; init; }

    public string? PauseReason { get; init; }

    public TaskSummaryDto ToSummaryDto(long workedSeconds) => new(
        Id,
        TaskKey.Format(ProjectKey, Number),
        Number,
        Title,
        ColumnId,
        ColumnName,
        ColumnKind,
        Priority,
        DueDate,
        Assignees,
        CompletedAt,
        ArchivedAt,
        UpdatedAt,
        EstimatedMinutes,
        workedSeconds,
        WorkStartedAt is not null,
        PauseReason);

    public MyTaskDto ToMyTaskDto(long workedSeconds) => new(
        Id,
        ProjectId,
        ProjectKey,
        ProjectName,
        TaskKey.Format(ProjectKey, Number),
        Number,
        Title,
        ColumnId,
        ColumnName,
        ColumnKind,
        Priority,
        DueDate,
        Assignees,
        CompletedAt,
        ArchivedAt,
        UpdatedAt,
        EstimatedMinutes,
        workedSeconds,
        WorkStartedAt is not null,
        PauseReason);
}
