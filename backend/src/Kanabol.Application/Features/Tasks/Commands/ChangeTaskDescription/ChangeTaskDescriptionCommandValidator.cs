using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;

public sealed class ChangeTaskDescriptionCommandValidator : AbstractValidator<ChangeTaskDescriptionCommand>
{
    public ChangeTaskDescriptionCommandValidator() => RuleFor(command => command.Description).IsATaskDescription();
}
