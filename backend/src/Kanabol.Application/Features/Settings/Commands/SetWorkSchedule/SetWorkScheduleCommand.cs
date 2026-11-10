using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;

/// <summary>Other working days, as ISO numbers, and hours as HH:mm, the start before the end (SPEC section 4.4).</summary>
public sealed record SetWorkScheduleCommand(IReadOnlyList<int> Days, string Start, string End) : ICommand<WorkScheduleDto>;
