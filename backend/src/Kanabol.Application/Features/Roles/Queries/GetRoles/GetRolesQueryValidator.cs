using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Roles.Queries.GetRoles;

public sealed class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator()
    {
        RuleFor(query => query.PageNumber).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
