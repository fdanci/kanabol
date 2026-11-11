using FluentValidation;

namespace Kanabol.Application.Features.Reports.Queries.GetWorkTimeReport;

public sealed class GetWorkTimeReportQueryValidator : AbstractValidator<GetWorkTimeReportQuery>
{
    public GetWorkTimeReportQueryValidator()
    {
        this.HasAnInterval();
    }
}
