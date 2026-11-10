using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.MoveTask;

/// <summary>
/// To a place in a column of the task's project, from 0. Without one, or past the end, it goes last. A task in
/// În lucru or after it never goes back before it (SPEC section 4.4).
/// </summary>
/// <remarks>EstimatedMinutes counts only when the task enters În lucru: needed then for one without an estimate, replacing it for one with.</remarks>
public sealed record MoveTaskCommand(Guid TaskId, Guid ColumnId, int? Position, int? EstimatedMinutes = null) : ICommand<TaskItemDto>;
