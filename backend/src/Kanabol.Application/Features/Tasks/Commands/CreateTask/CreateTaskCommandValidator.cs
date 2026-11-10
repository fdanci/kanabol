using FluentValidation;

namespace Kanabol.Application.Features.Tasks.Commands.CreateTask;

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(command => command.Title).IsATaskTitle();
        RuleFor(command => command.Description).IsATaskDescription();
        RuleFor(command => command.Priority).IsATaskPriority();
        RuleFor(command => command.AssigneeIds).AreAssigneesEachOnce();
        RuleFor(command => command.EstimatedMinutes).IsAnEstimate();
    }
}
