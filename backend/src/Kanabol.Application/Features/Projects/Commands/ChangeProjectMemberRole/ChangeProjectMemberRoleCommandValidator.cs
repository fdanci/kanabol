using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;

public sealed class ChangeProjectMemberRoleCommandValidator : AbstractValidator<ChangeProjectMemberRoleCommand>
{
    public ChangeProjectMemberRoleCommandValidator() => RuleFor(command => command.Role).IsAProjectRole();
}
