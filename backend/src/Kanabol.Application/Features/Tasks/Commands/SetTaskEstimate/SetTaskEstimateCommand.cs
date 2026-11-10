using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskEstimate;

/// <summary>Another estimate in minutes, any time; null takes it away, but only before the task first entered În lucru.</summary>
public sealed record SetTaskEstimateCommand(Guid TaskId, int? EstimatedMinutes) : ICommand<TaskItemDto>;
