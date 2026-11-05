using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Auth.Commands.LoginUser;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    private const int MaximumEmailLength = 320;

    public LoginUserCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(MaximumEmailLength).HasNoHiddenCharacters();
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
    }
}
