using FluentValidation;
using Kanabol.Application.Features.Auth;

namespace Kanabol.Application.Features.Users.Commands.ResetUserPassword;

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator() => RuleFor(command => command.InitialPassword).MeetsPasswordPolicy();
}
