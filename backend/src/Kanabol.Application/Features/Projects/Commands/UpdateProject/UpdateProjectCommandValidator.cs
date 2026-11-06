using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.UpdateProject;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(command => command.Name).IsAProjectName();
        RuleFor(command => command.Description).IsAProjectDescription();
    }
}
