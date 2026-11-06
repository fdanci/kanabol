using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;

public sealed record SetTaskPriorityCommand(Guid TaskId, TaskPriority Priority) : ICommand<TaskItemDto>;
