using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.RestoreTask;

public sealed record RestoreTaskCommand(Guid TaskId) : ICommand<TaskItemDto>;
