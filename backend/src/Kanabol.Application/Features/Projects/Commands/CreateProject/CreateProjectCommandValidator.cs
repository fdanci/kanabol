using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.CreateProject;

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(command => command.Name).IsAProjectName();
        RuleFor(command => command.Key).IsAProjectKey();
        RuleFor(command => command.Description).IsAProjectDescription();
    }
}
