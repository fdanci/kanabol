using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.AssignTask;

/// <summary>The whole list, which takes the place of the one before: an empty one leaves the task to nobody.</summary>
public sealed record AssignTaskCommand(Guid TaskId, IReadOnlyList<Guid> AssigneeIds) : ICommand<TaskItemDto>;
