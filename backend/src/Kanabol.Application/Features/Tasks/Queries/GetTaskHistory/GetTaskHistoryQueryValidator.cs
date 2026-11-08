using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

public sealed class GetTaskHistoryQueryValidator : AbstractValidator<GetTaskHistoryQuery>
{
    public GetTaskHistoryQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();
    }
}
