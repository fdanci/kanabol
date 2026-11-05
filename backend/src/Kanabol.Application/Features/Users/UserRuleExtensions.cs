using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Domain.Users;

namespace Kanabol.Application.Features.Users;

public static class UserRuleExtensions
{
    public static IRuleBuilderOptions<T, string> IsAnEmail<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(User.MaxEmailLength)
            .HasNoHiddenCharacters()
            .Must(email => HasTheShapeOfAnEmail(email.Trim()))
            .WithMessage("This isn't an email address.");

    public static IRuleBuilderOptions<T, string> IsADisplayName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(User.MaxDisplayNameLength).HasNoHiddenCharacters();

    /// <summary>Only the shape: the address is what someone signs in with, and nothing is ever sent to it.</summary>
    private static bool HasTheShapeOfAnEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at < email.Length - 1
            && at == email.LastIndexOf('@')
            && !email.Any(char.IsWhiteSpace);
    }
}
