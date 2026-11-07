using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.AssignTask;

public sealed class AssignTaskCommandValidator : AbstractValidator<AssignTaskCommand>
{
    public AssignTaskCommandValidator()
    {
        RuleFor(command => command.AssigneeIds).AreAssigneesEachOnce();
    }
}
