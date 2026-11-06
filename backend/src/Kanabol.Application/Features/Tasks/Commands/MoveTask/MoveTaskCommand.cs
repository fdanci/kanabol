using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.MoveTask;

/// <summary>To a place in a column of the task's project, from 0. Without one, or past the end, it goes last.</summary>
public sealed record MoveTaskCommand(Guid TaskId, Guid ColumnId, int? Position) : ICommand<TaskItemDto>;
