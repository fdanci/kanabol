using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Users.Queries.GetUsers;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(query => query.PageNumber).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
