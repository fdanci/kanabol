using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.AssignTask;

/// <summary>Null leaves the task to nobody.</summary>
public sealed record AssignTaskCommand(Guid TaskId, Guid? AssigneeId) : ICommand<TaskItemDto>;
