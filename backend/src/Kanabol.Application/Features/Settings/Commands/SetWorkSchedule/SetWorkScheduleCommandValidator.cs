using FluentValidation;
using Kanabol.Domain.Settings;

namespace Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;

public sealed class SetWorkScheduleCommandValidator : AbstractValidator<SetWorkScheduleCommand>
{
    public SetWorkScheduleCommandValidator()
    {
        RuleFor(command => command.Days)
            .Must(days => WorkSchedule.AreWorkDays(days))
            .WithMessage("A schedule has at least one day, each once, from 1 for Monday to 7 for Sunday.");
        RuleFor(command => command.Start)
            .Must(start => WorkSchedules.TryParseTime(start, out _))
            .WithMessage("An hour is HH:mm, from 00:00 to 23:59.");
        RuleFor(command => command.End)
            .Cascade(CascadeMode.Stop)
            .Must(end => WorkSchedules.TryParseTime(end, out _))
            .WithMessage("An hour is HH:mm, from 00:00 to 23:59.")
            .Must((command, end) => !WorkSchedules.TryParseTime(command.Start, out var start)
                || (WorkSchedules.TryParseTime(end, out var parsed) && start < parsed))
            .WithMessage("The start comes before the end.");
    }
}
