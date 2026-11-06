using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;

/// <summary>Basic markdown. Null or blank clears it.</summary>
public sealed record ChangeTaskDescriptionCommand(Guid TaskId, string? Description) : ICommand<TaskItemDto>;
