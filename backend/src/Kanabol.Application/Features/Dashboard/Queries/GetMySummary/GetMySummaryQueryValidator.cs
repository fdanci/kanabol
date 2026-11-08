using FluentValidation;
using Kanabol.Application.Abstractions;

namespace Kanabol.Application.Features.Dashboard.Queries.GetMySummary;

public sealed class GetMySummaryQueryValidator : AbstractValidator<GetMySummaryQuery>
{
    public GetMySummaryQueryValidator(IClock clock)
    {
        RuleFor(query => query.WeekStart)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(weekStart => weekStart <= clock.UtcNow)
            .WithMessage("The week can't start after now.");
    }
}
