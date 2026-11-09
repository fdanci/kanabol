using FluentValidation;

namespace Kanabol.Application.Features.Reports.Queries.GetCompletedReport;

public sealed class GetCompletedReportQueryValidator : AbstractValidator<GetCompletedReportQuery>
{
    public GetCompletedReportQueryValidator()
    {
        this.HasAnInterval();
        RuleFor(query => query.Granularity).IsInEnum();
    }
}
