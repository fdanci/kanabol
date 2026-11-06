using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskDueDate;

/// <summary>Null clears the deadline. It's stored in UTC.</summary>
public sealed record SetTaskDueDateCommand(Guid TaskId, DateTimeOffset? DueDate) : ICommand<TaskItemDto>;
