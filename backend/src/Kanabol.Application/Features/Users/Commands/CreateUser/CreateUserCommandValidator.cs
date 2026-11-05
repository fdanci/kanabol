using FluentValidation;
using Kanabol.Application.Features.Auth;

namespace Kanabol.Application.Features.Users.Commands.CreateUser;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Email).IsAnEmail();
        RuleFor(command => command.DisplayName).IsADisplayName();
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.InitialPassword).MeetsPasswordPolicy();
    }
}
