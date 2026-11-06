using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.AddProjectMember;

public sealed class AddProjectMemberCommandValidator : AbstractValidator<AddProjectMemberCommand>
{
    public AddProjectMemberCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Role).IsAProjectRole();
    }
}
