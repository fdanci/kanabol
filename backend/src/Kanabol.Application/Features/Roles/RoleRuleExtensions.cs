using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Domain.Authorization;

namespace Kanabol.Application.Features.Roles;

public static class RoleRuleExtensions
{
    public static IRuleBuilderOptions<T, string> IsARoleName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(Role.MaxNameLength).HasNoHiddenCharacters();

    public static IRuleBuilderOptions<T, string> IsAPermission<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(permission => Permissions.All.Contains(permission)).WithMessage("This permission doesn't exist.");
}
