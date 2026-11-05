using FluentValidation;

namespace Kanabol.Application.Features.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.NewPassword).MeetsPasswordPolicy();
    }
}
