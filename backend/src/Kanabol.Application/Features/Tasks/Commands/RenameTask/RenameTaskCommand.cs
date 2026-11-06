using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.RenameTask;

public sealed record RenameTaskCommand(Guid TaskId, string Title) : ICommand<TaskItemDto>;
