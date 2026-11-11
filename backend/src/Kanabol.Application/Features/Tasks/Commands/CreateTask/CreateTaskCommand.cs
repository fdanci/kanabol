using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Commands.CreateTask;

/// <summary>
/// A task starts in Backlog, at its end, assigned to any number of people: a column, when given, has to be the
/// project's Backlog (SPEC section 4.4).
/// </summary>
public sealed record CreateTaskCommand(
    Guid ProjectId,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? ColumnId,
    IReadOnlyList<Guid> AssigneeIds,
    DateTimeOffset? DueDate,
    int? EstimatedMinutes = null) : ICommand<TaskItemDto>;
