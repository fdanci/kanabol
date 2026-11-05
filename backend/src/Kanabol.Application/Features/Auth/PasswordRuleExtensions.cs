using FluentValidation;

namespace Kanabol.Application.Features.Auth;

public static class PasswordRuleExtensions
{
    public static IRuleBuilderOptions<T, string> MeetsPasswordPolicy<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinimumLength)
            .MaximumLength(PasswordPolicy.MaximumLength)
            .Must(password => !PasswordPolicy.IsCommon(password))
            .WithMessage("This password is too common.");
}
