using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskEstimate;

public sealed class SetTaskEstimateCommandValidator : AbstractValidator<SetTaskEstimateCommand>
{
    public SetTaskEstimateCommandValidator() => RuleFor(command => command.EstimatedMinutes).IsAnEstimate();
}
