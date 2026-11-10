using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Queries.GetArchivedTasks;

public sealed class GetArchivedTasksQueryValidator : AbstractValidator<GetArchivedTasksQuery>
{
    public GetArchivedTasksQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
