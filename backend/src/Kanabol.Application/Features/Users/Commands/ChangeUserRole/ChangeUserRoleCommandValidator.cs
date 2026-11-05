using FluentValidation;

namespace Kanabol.Application.Features.Users.Commands.ChangeUserRole;

public sealed class ChangeUserRoleCommandValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleCommandValidator() => RuleFor(command => command.RoleId).NotEmpty();
}
