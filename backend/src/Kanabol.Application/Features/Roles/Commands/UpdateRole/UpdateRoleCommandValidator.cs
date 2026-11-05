using FluentValidation;

namespace Kanabol.Application.Features.Roles.Commands.UpdateRole;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(command => command.Name).IsARoleName();
        RuleForEach(command => command.Permissions).IsAPermission();
    }
}
