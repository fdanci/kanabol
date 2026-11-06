using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.MoveTask;

public sealed class MoveTaskCommandValidator : AbstractValidator<MoveTaskCommand>
{
    public MoveTaskCommandValidator() => RuleFor(command => command.Position).GreaterThanOrEqualTo(0);
}
