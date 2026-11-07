using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Commands.CreateTask;

/// <summary>Without a column, the task starts in the board's first one. It goes at the end of its column, assigned to any number of people.</summary>
public sealed record CreateTaskCommand(
    Guid ProjectId,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? ColumnId,
    IReadOnlyList<Guid> AssigneeIds,
    DateTimeOffset? DueDate) : ICommand<TaskItemDto>;
