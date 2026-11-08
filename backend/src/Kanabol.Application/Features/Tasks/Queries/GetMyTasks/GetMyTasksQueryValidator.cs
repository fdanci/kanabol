using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetMyTasks;

public sealed class GetMyTasksQueryValidator : AbstractValidator<GetMyTasksQuery>
{
    public GetMyTasksQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
        RuleFor(query => query.Sort)
            .Must(sort => TaskSort.TryParse(sort, out _))
            .WithMessage($"Sort by one of {string.Join(", ", TaskSort.Fields)}, then :asc or :desc.");
        RuleForEach(query => query.Priorities).IsInEnum();
        RuleFor(query => query.Search!).MaximumLength(GetMyTasksQuery.MaxSearchLength).HasNoHiddenCharacters();
    }
}
