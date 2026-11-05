using FluentValidation;

namespace Kanabol.Application.Features.Roles.Commands.CreateRole;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Name).IsARoleName();
        RuleForEach(command => command.Permissions).IsAPermission();
    }
}
