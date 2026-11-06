using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.SetTaskPriority;

public sealed class SetTaskPriorityCommandValidator : AbstractValidator<SetTaskPriorityCommand>
{
    public SetTaskPriorityCommandValidator() => RuleFor(command => command.Priority).IsATaskPriority();
}
