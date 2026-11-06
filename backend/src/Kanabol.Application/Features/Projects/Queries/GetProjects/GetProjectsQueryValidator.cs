using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Queries.GetProjects;

public sealed class GetProjectsQueryValidator : AbstractValidator<GetProjectsQuery>
{
    public GetProjectsQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
