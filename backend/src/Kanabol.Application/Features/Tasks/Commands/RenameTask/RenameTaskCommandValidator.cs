using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.RenameTask;

public sealed class RenameTaskCommandValidator : AbstractValidator<RenameTaskCommand>
{
    public RenameTaskCommandValidator() => RuleFor(command => command.Title).IsATaskTitle();
}
