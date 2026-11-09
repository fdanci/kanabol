namespace Kanabol.Application.Features.Reports;

/// <summary>
/// What the three reports take alike (SPEC section 5.1.1): an interval [From, To), required, From before To
/// and at most 366 days apart, and a project and a person, both optional.
/// </summary>
public interface IReportFilter
{
    DateTimeOffset? From { get; }

    DateTimeOffset? To { get; }

    Guid? ProjectId { get; }

    Guid? UserId { get; }
}
