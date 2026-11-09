using FluentValidation;

namespace Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;

public sealed class GetCycleTimeReportQueryValidator : AbstractValidator<GetCycleTimeReportQuery>
{
    public GetCycleTimeReportQueryValidator()
    {
        this.HasAnInterval();
    }
}
