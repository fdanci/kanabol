using FluentValidation;

namespace Kanabol.Application.Features.Reports.Queries.GetDeadlinesReport;

public sealed class GetDeadlinesReportQueryValidator : AbstractValidator<GetDeadlinesReportQuery>
{
    public GetDeadlinesReportQueryValidator()
    {
        this.HasAnInterval();
        RuleFor(query => query.Granularity).IsInEnum();
    }
}
