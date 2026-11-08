using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;

/// <summary>
/// One run of the deadline alerts (SPEC section 4.3), which OverdueTasksWorker sends at start and then every
/// Alerts:OverdueCheckInterval, without a user. It answers how many notifications it made.
/// </summary>
public sealed record NotifyOverdueTasksCommand : ICommand<int>;
