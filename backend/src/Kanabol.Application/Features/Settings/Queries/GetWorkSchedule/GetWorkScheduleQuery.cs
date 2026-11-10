using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Settings.Queries.GetWorkSchedule;

/// <summary>The work schedule, for anyone signed in: the board and the reports say it.</summary>
public sealed record GetWorkScheduleQuery : IQuery<WorkScheduleDto>;
