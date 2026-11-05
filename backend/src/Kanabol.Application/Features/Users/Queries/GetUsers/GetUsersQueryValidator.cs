using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Users.Queries.GetUsers;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
